# 多模态 Prompt 与长期记忆检索实现方案

> 状态：Accepted v0.1
>
> 日期：2026-07-31
>
> 适用范围：截图感知、牌局短期记忆、攻略长期记忆、策略决策与解释

## 1. 目标与结论

MahjongAgent 的核心不是让一个多模态模型“看图后直接出牌”，而是把不稳定的视觉候选，转换成可校验、可回放的牌局事实，再由确定性规则、数值策略和经过检索的长期经验共同产生建议。

首版采用以下边界：

1. 多模态模型只负责感知，输出候选 `Observation`，不直接修改 `GameState`；
2. 本局事实使用 `GameEvent + GameState` 保存，不依赖模型上下文窗口；
3. 胡牌合法性、向听、有效牌、番数和硬规则由 C# 计算；
4. 长期攻略采用结构化过滤、FTS5/BM25 和可选 Embedding 的混合检索；
5. 攻略只能调整策略软评分和解释，不能覆盖合法性与牌数守恒；
6. LLM 最终只解释已经完成的决策，不重新选择动作；
7. 实时核心使用 C# 强类型状态机，不引入 LangGraph 作为权威运行时。

## 2. 三条隔离链路

```text
截图 + VisualProfile + 已确认状态
  -> 感知 Prompt
  -> Observation 候选
  -> 本地 Schema / 时序 / 牌数 / 规则校验
  -> GameEvent
  -> GameState

GameState + 规则计算 + 风险特征
  -> 长期记忆混合检索
  -> C# StrategyEngine
  -> DecisionSnapshot

DecisionSnapshot + 评分明细 + 记忆引用 + 不确定性
  -> 解释 Prompt
  -> 悬浮助手文案
```

### 2.1 感知链路

感知 Prompt 可以接收：

- 当前一张或少量相邻截图；
- `VisualProfile` 的布局、皮肤和区域摘要；
- `RuleProfile` 中与画面语义有关的基础信息；
- 上一份已经确认的 `GameState` 摘要；
- 最近关键事件；
- 本次变化区域；
- 当前需要澄清的不确定项。

感知 Prompt 禁止接收：

- 推荐出牌；
- 攻略结论；
- 对手已经听某张牌等未经确认的推断；
- 为了让画面“符合策略”而给出的期望答案。

这是防止确认偏差的关键。例如策略层认为对家可能做清一色，不能反过来提示视觉模型“对家正在做清一色”，否则模型可能把模糊牌面强行识别成同一花色。

### 2.2 策略链路

策略层只读取已经确认的状态和显式推断：

- `GameState` 与规则版本；
- 合法动作集合；
- 向听数、有效进张、剩余牌数；
- 癞子、皮、开口、承包和番数特征；
- 对手风险特征及其置信度；
- 长期攻略检索结果及引用。

策略引擎先执行硬约束，再执行数值评分。长期记忆只能以有上限的权重调整软评分。任何攻略都不能使非法动作成为候选，也不能修改已确认牌局事实。

### 2.3 解释链路

解释模型只接收最终 `DecisionSnapshot`，包括推荐动作、备选动作、评分分解、风险、检索引用和不确定性。它的职责是把计算结果压缩成用户能快速理解的提示，不得重新排序候选或创造新的牌局事实。

## 3. PromptPackage

每一次模型调用都必须生成可审计的 `PromptPackage`。至少记录：

```text
PromptVersion
SchemaVersion
Mode: calibrate | observe | recover
RuleProfileVersion
GameStateRevision
VisualProfileVersion
MemoryVersion（策略/解释链路；感知链路为空）
KnownFacts
Uncertainties
RecentEvents
ChangedRegions
RetrievedMemories（策略/解释链路；感知链路必须为空）
Images / FrameIds
PromptBudget 与实际裁剪统计
```

`PromptVersion` 和 `SchemaVersion` 分开演进。修改措辞但不修改输出字段时只升级 Prompt；修改输出结构时升级 Schema，并同时提供迁移和固定样本回归结果。

### 3.1 三种感知模式

| 模式 | 触发条件 | 图像范围 | 主要目标 |
|---|---|---|---|
| `calibrate` | 陌生窗口或新皮肤 | 完整牌桌 | 识别布局、区域、牌面风格和可见状态 |
| `observe` | 已有可靠 VisualProfile | 变化区域为主 | 识别相对上一状态发生的最小变化 |
| `recover` | 牌数矛盾、窗口漂移、长时间丢帧 | 完整牌桌加必要局部图 | 重新建立与已确认事实一致的候选状态 |

模式是显式状态，不允许模型自行切换。`recover` 也不能覆盖历史事件，只能产生供 Resolver 比较的候选观察。

### 3.2 上下文预算

Prompt 采用确定性的字符预算和条目预算，后续可基于真实 Provider token 统计校准。优先级如下：

不可裁剪：

1. 系统边界和注入防护；
2. 当前工作模式；
3. 输出 JSON Schema；
4. 关键规则约束；
5. 标记为 required 的已确认事实；
6. 阻断决策的关键不确定项。

按预算裁剪：

7. 最近关键事件；
8. 普通不确定项和变化区域；
9. VisualProfile 细节；
10. 策略/解释链路的少量高相关检索；
11. 一至两个已经确认的示例。

同一优先级按稳定的输入顺序裁剪，使相同状态必然生成相同 Prompt，便于缓存和回归测试。如果不可裁剪内容已经超出预算，必须拒绝调用并报告预算错误，不能静默截断 JSON。

### 3.3 Prompt 注入防护

截图中的聊天文字、昵称、房间名、视频字幕，以及历史文本都属于不可信数据：

- 不可信内容只放进序列化的 `context_json`，不拼接为系统指令；
- 系统 Prompt 明确要求忽略图像和上下文中的指令性文字；
- Schema 限制字段、长度、数量和枚举；
- Provider 返回后再做强类型反序列化和本地语义校验；
- 原始响应不能直接进入日志、长期记忆或 UI 富文本渲染。

## 4. Observation 契约

模型返回的是 `MahjongObservationBatch`，而不是完整 `GameState`。首版结构包含：

```json
{
  "schema_version": "mahjong-observation/v1",
  "mode": "observe",
  "frame_ids": ["frame-1024"],
  "observations": [
    {
      "observation_id": "obs-1",
      "entity": "tile",
      "action": "appeared",
      "region": "self_hand",
      "candidates": [
        { "value": "5p", "confidence": 0.97 }
      ],
      "confidence": 0.97,
      "uncertain": false,
      "evidence": {
        "frame_ids": ["frame-1024"],
        "visual_cues": ["bottom row, fifth position"]
      }
    }
  ],
  "uncertainties": []
}
```

约束：

- 不确定时返回多个候选或空候选并标记 `uncertain`，不允许编造单一答案；
- `tile` 与 `indicator` 的候选值必须使用统一编码 `1m..9m / 1p..9p / 1s..9s / 1z..7z`；
- evidence 中的 frame ID 必须来自当前请求；
- mode 必须与请求模式一致；
- 未声明字段、非法枚举、越界置信度、重复 ID 和过长字符串均拒绝；
- 通过契约校验只说明“格式可信”，仍需 Resolver 做多帧、时序、牌数和规则校验。

## 5. 本局短期记忆

短期记忆不是一段不断增长的聊天记录，而是四层数据：

```text
Frame ring buffer        最近数秒截图，仅内存
ObservationEvidence      模型候选与证据，可审计
GameEvent                经过 Resolver 接受的追加事实
GameState                从事件归并得到的可重建快照
```

`GameState` 至少覆盖：

- 当前巡目、牌局阶段和行动玩家；
- 自家手牌、刚摸牌、弃牌、副露和开口状态；
- 四家弃牌河、副露、杠和公开动作；
- 癞子指示牌、癞子、皮和规则变体；
- 已见牌计数与牌数守恒状态；
- 番数、承包关系及其计算依据；
- 各对手牌型倾向、听牌可能性和风险置信度；
- 未解决 Observation 分支；
- 历史建议与当时的 `DecisionSnapshot`。

模型上下文只接收这份状态的任务相关摘要。完整事实保存在本地事件流中，因此程序重启、Provider 切换或上下文裁剪不会丢失整局记忆。

## 6. 长期记忆模型

长期记忆分为三类，不能混在一个向量集合里：

1. 规则知识：规则条款、版本、适用平台和来源；
2. 攻略知识：经过验证的战术原则、牌型案例和常见错误；
3. 情景经验：历史 `Episode`，包含当时状态、推荐、实际动作和结果，但结果不自动等于正确标签。

每条记录至少包含：

```text
id / namespace / memory_type / status
content_json / normalized_text
ruleset_version / strategy_version / model_version
structured_features
confidence / quality_score
valid_from / valid_to / superseded_by
source_ids / created_at / reviewed_at
embedding_version（可空）
```

状态为 `candidate / confirmed / rejected / superseded`。正式检索默认只使用 `confirmed`；规则来源有冲突时保留多版本，不覆盖旧记录。

## 7. 混合检索管线

麻将的结构相似比自然语言相似更重要。查询顺序固定为：

1. 精确过滤 `RuleSet + version`；
2. 过滤巡目、向听、癞子数量、开口状态和牌局阶段；
3. 过滤手牌结构、候选动作和剩余牌特征；
4. 过滤对手风险模式；
5. 使用 SQLite FTS5/BM25 召回关键词候选；
6. 有 Embedding 时在小候选集内重排；
7. 删除冲突、失效、低质量、低置信度和已 superseded 条目；
8. 做来源多样化，返回少量 Top-K；
9. 将查询、候选、分项得分、淘汰原因和最终注入项写入 `retrieval_audit`。

建议首版评分：

```text
score =
  ruleset_match
  + structural_similarity
  + bm25_score
  + semantic_similarity
  + quality_score
  + temporal_validity
  - conflict_penalty
  - low_confidence_penalty
```

结构过滤无法命中的记录，即使语义向量非常相似也不能进入最终 Top-K。

## 8. 决策融合边界

策略评分分为：

```text
HardGate(action)              合法性、牌数、规则约束
BaseScore(action)             向听、有效牌、剩余张数、预期收益
RiskScore(action)             放铳、承包、喂下家、牌局阶段
MemoryAdjustment(action)      经验证攻略的有限调整
FinalScore(action)            可审计的加权结果
```

`MemoryAdjustment` 必须设置绝对上限，并记录每一条来源。若检索结果互相冲突，降低调整幅度或退回 `BaseScore + RiskScore`，不能让解释模型自行裁决。

## 9. 是否引入开源框架

### 9.1 不使用 LangGraph 作为核心

实时牌局有明确状态、严格顺序、低延迟和持久化事务要求。现有 C# `AgentStateMachine`、`GameEvent`、`GameState`、规则引擎和 Strategy Engine 更适合作为权威层。引入 Python/LangGraph 会增加跨进程状态一致性、部署、调试和 checkpoint 双写成本，且不能替代麻将规则实现。

### 9.2 选择性采用的组件

- `Microsoft.Extensions.AI`：统一文本、Embedding 和遥测抽象；
- `Microsoft.Extensions.Http.Resilience`：超时、退避、熔断和限流；
- OpenTelemetry：Prompt 版本、Provider 延迟、token、拒绝率和检索指标；
- `System.Text.Json` 强类型 DTO；复杂 Schema 需求明确后再评估 JsonSchema.Net；
- SQLite FTS5：首版全文检索；
- 可替换的 `IEmbeddingProvider`：首版允许关闭 Embedding。

Semantic Kernel 可在以后用于模板和插件适配，但不使用 Planner 控制牌局。Mem0、Graphiti、Cognee 等项目可作为记忆生命周期设计参考，不作为正式牌局事实库。

## 10. 数据表与审计

本轮设计对应的后续 SQLite 表：

```text
prompt_executions
observations
observation_evidence
game_sessions
game_events
state_snapshots
decision_snapshots
knowledge_entries
knowledge_fts
memory_embeddings
retrieval_audit
retrieval_audit_candidates
```

`prompt_executions` 不默认保存完整截图，应保存 Prompt/Schema 版本、上下文摘要哈希、frame ID、Provider、模型、耗时、token、结果状态和错误分类。诊断模式需要保存裁剪截图时，必须由用户显式开启并提供清理入口。

## 11. 评测门槛

### 感知

- 固定截图序列上的实体和事件准确率；
- 同一序列重复运行一致性；
- 非法 Observation 本地拒绝率 100%；
- mode、frame ID 和 Schema 不匹配拒绝率 100%；
- Prompt 注入样本不能改变输出契约或系统边界。

### 短期记忆

- 从完整事件重放得到相同 `GameState`；
- 牌数守恒与重复事件检测；
- 丢帧、动画、窗口漂移后的恢复成功率；
- 一局内需要人工纠错的次数。

### 检索与决策

- 结构过滤 Recall@K、MRR 和来源命中率；
- 规则版本串库率必须为 0；
- 建议合法率必须为 100%；
- 解释与 `DecisionSnapshot` 一致率；
- 开启长期记忆前后的固定牌局收益和放铳风险对比。

## 12. 实施顺序

### M1：Prompt 与 Observation 基础

- `PerceptionMode`、`PromptPackage`、`PromptBudget`；
- `KnownFact`、`Uncertainty`、`RecentEventSummary`、`ChangedRegion`；
- `PerceptionPromptComposer`；
- `MahjongObservationBatch`、JSON Schema 和本地强类型验证；
- 将 WPF 单截图预览切换到新契约。

### M2：短期记忆闭环

- `ObservationResolver`；
- `GameEvent / GameState / Reducer`；
- SQLite 事件存储和 checkpoint；
- 固定截图序列与事件重放测试。

### M3：规则与策略

- 武汉红中赖子杠可组合 `RuleProfile`；
- 合法动作、胡牌、向听、有效牌、番数和承包；
- `DecisionSnapshot` 和悬浮提示策略。

### M4：长期攻略检索

- 知识导入、来源和审核状态；
- SQLite FTS5 与结构化过滤；
- `retrieval_audit`；
- 可选 Embedding 重排和离线指标。

### M5：受控学习

- 用户纠错形成候选 VisualProfile；
- 牌局 Episode 后台整理；
- 固定评测、影子策略、版本晋升和回滚；
- 未经验证的经验永不自动进入正式策略。

## 13. 本轮代码验收标准

本设计的第一批代码必须满足：

1. UI 不再手写感知 Prompt 和预览 Schema；
2. 三种模式有不同、可测试的任务指令；
3. 上下文按优先级确定性裁剪，required 事实不会静默丢失；
4. 感知包中不存在攻略检索入口；
5. 返回 JSON 经强类型反序列化、未知字段拒绝和本地语义校验；
6. tile 候选只接受标准牌编码；
7. mode 和 frame ID 与请求不一致时拒绝；
8. 全量 Release 测试和构建通过。

## 14. 实现进度

截至 2026-07-31：

- M1 已完成：版本化感知 Prompt、预算裁剪、三种感知模式、Observation v1 和本地强类型校验；
- M2 基础已完成：`GameEvent`、`GameState`、确定性 Reducer、两帧确认和 Observation Resolver 事实门控；
- Reducer 已执行事件顺序、跨局、时间、手牌结构、牌数上限、弃牌归属和副露移动校验；
- Resolver 只晋升已映射区域中的高置信度单一候选，描述性观察继续保留为 Observation；
- 对手隐藏摸牌只记录未知张数，不允许从视觉提示晋升为具体牌面；
- 尚未完成：SQLite 事件存储、状态快照、应用重启恢复、持续变化检测和副露 Observation 聚合。
