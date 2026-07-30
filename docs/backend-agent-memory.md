# Agent 与长期记忆后端设计方案

> 状态：Draft v0.1
> 日期：2026-07-30
> 适用范围：MahjongAgent 的感知编排、牌局状态、策略、长期记忆、检索与自学习

## 1. 设计目标

后端系统需要同时满足：

- 从不稳定的多模态视觉中重建可追溯的牌局事实；
- 对每个建议给出合法、可验证、可解释的计算依据；
- 在不阻塞牌局实时提示的前提下形成长期记忆；
- 从历史牌局中检索真正相似的经验，而不是只做自然语言相似；
- 允许 Agent 生成候选经验，但不允许未经验证地修改规则和策略；
- 所有记忆可查看、可审计、可版本化、可回滚和可删除；
- Windows Demo 保持单机、轻量和隐私可控。

## 2. 核心决策

1. 运行时采用纯 C#/.NET 10，不依赖 Python Agent 服务。
2. 不直接引入 LangGraph、Mem0、Graphiti、Letta 或 Cognee 作为核心运行时。
3. 吸收它们的记忆分层、混合检索、后台整合、时间有效期和来源追踪设计。
4. SQLite 是 Demo 的唯一持久化数据库。
5. `GameEvent` 是本局唯一事实来源，`GameState` 是可重建快照。
6. 规则与牌效由确定性代码计算，LLM 不拥有最终决策权。
7. LLM 只负责视觉候选、解释、追问和后台候选记忆提炼。
8. 长期经验必须经过规则验证和固定评测集验证后才能晋升。

## 3. 技术选型

| 能力 | 选型 |
|---|---|
| 运行时 | C# / .NET 10 |
| 后台服务 | `Microsoft.Extensions.Hosting.BackgroundService` |
| 进程内事件流 | `System.Threading.Channels` |
| 模型抽象 | `Microsoft.Extensions.AI` + Provider 适配器 |
| JSON | `System.Text.Json` + 强类型 DTO + JSON Schema |
| 数据库 | SQLite |
| 数据访问 | `Microsoft.Data.Sqlite` + Dapper |
| 数据迁移 | 内嵌、编号、可重复验证的 SQL 脚本 |
| 关键词检索 | SQLite FTS5 / BM25 |
| 向量检索 | 首版 SQLite BLOB + C# 进程内余弦重排 |
| 未来向量扩展 | `sqlite-vec` 或外部适配器，评测后启用 |
| 时序关系 | SQLite `memory_links` + `valid_from/valid_to` |
| 日志 | Serilog |
| 追踪与指标 | OpenTelemetry |
| HTTP 韧性 | `Microsoft.Extensions.Http.Resilience` |
| 密钥 | Windows Credential Manager / DPAPI |

## 4. 总体运行链路

```text
FrameChanged
  → MultimodalPerceptionProvider
  → ObservationBatch（候选）
  → ObservationResolver
  → GameEvent（正式事实）
  → GameStateReducer
  → StrategyEngine
  → MemoryRetriever
  → DecisionSnapshot
  → AlertPolicy
  → 主窗口与悬浮助手
```

牌局结束后的后台链路：

```text
RoundEnded
  → EpisodeBuilder
  → MemoryConsolidator
  → MemoryCandidate
  → ConflictDetector
  → RuleValidator
  → StrategyReplayEvaluator
  → candidate / confirmed / rejected / superseded
```

## 5. Agent 编排

首版采用单 Agent 编排器，不使用多个 LLM Agent 互相讨论。

```text
Idle
→ TargetDetected
→ Calibrating
→ WaitingForRound
→ Observing
→ ResolvingObservation
→ UpdatingState
→ EvaluatingStrategy
→ PublishingAdvice
→ RoundEnded
→ ConsolidatingMemory
```

主要事件：

```text
FrameChanged
WindowLost
ObservationReceived
ObservationRejected
GameEventAccepted
SelfTurnDetected
OpponentRiskChanged
HighRiskDetected
RoundEnded
MemoryConsolidated
```

编排器只调度工具，不直接承担规则计算或数据库细节。

## 6. 模型 Provider

```csharp
public interface IMultimodalPerceptionProvider
{
  Task<ObservationBatch> ObserveAsync(
    PerceptionRequest request,
    CancellationToken cancellationToken);
}

public interface IAgentLanguageProvider
{
  Task<AgentExplanation> ExplainAsync(
    ExplanationRequest request,
    CancellationToken cancellationToken);
}

public interface IEmbeddingProvider
{
  Task<EmbeddingVector> EmbedAsync(
    string content,
    CancellationToken cancellationToken);
}
```

Provider 层要求：

- API、模型和 Base URL 可配置；
- 图片请求与文本请求分别计费、限流和重试；
- 强制结构化输出；
- 记录模型版本、提示版本、耗时、Token 和成本；
- API Key 不进入日志、SQLite 或诊断包；
- 可替换为未来本地 ONNX 感知实现。

## 7. 记忆分层

### 7.1 瞬时视觉记忆

最近数秒帧环形缓冲：

- 多帧确认；
- 动画稳定检测；
- 局部重新观察；
- 状态矛盾恢复。

默认不持久化。首次标定和恢复可能向配置的多模态服务发送经过遮罩的目标窗口画面，但不等于本地长期保存完整截图。

### 7.2 本局工作记忆

```text
GameEvent
GameState
StateSnapshot
ObservationEvidence
```

要求：

- 正式事件只追加；
- 错误通过补偿事件修正；
- 状态可以从事件重放；
- 定期生成快照加速恢复；
- 不使用向量数据库保存当前牌局事实。

### 7.3 情景记忆

每次重要决策形成 `Episode`：

```json
{
  "ruleset": "wuhan-koukoufan",
  "turn": 8,
  "state_snapshot_id": "state-083",
  "recommended_action": "9s",
  "alternatives": ["7z", "5p"],
  "actual_action": "7z",
  "features": {
    "shanten": 1,
    "effective_tile_count": 12,
    "wildcard_count": 1,
    "open_count": 2,
    "next_player_chi_risk": "medium"
  },
  "strategy_version": "0.1.0"
}
```

Episode 是经历，不自动代表正确经验。单局输赢不能直接作为策略标签。

### 7.4 语义记忆

保存经过验证的长期事实和知识：

- WindowProfile 视觉映射；
- 规则档案与适用变体；
- 已验证的战术模式；
- 对手行为统计；
- 用户提示偏好；
- 高质量典型牌局。

每条记忆必须包含来源、适用范围、可信度、时间和版本。

### 7.5 程序记忆

包括规则、牌效算法、策略权重、提示模板和检索配置。程序记忆通过 Git、代码评审和测试发布，Agent 无权在线修改。

## 8. 数据库设计

### 8.1 本局与感知

```text
game_sessions
game_events
state_snapshots
observations
observation_evidence
decision_snapshots
```

### 8.2 长期记忆

```text
episodes
episode_features
episode_actions
episode_outcomes
memory_facts
memory_links
memory_versions
memory_sources
memory_embeddings
consolidation_jobs
retrieval_audit
```

### 8.3 MemoryRecord 必要字段

```text
id
namespace
memory_type
status
content_json
confidence
quality_score
valid_from
valid_to
created_at
source_event_ids
ruleset_version
strategy_version
model_version
superseded_by
```

状态：

```text
candidate
confirmed
rejected
superseded
```

原始 Episode 和推导 Memory 分离，推导结果必须能够追溯到原始事件。

## 9. 检索架构

麻将场景中，牌局结构相似度优先于自然语言相似度。

```text
1. 规则集精确过滤
2. 牌局阶段与结构特征过滤
3. 向听、癞子、开口、巡目和对手模式过滤
4. FTS5 / BM25
5. Embedding 相似度
6. 关系扩展
7. 时间、质量、可信度和冲突重排
8. 返回多样化 Top-K
```

```csharp
public interface IMemoryRetriever
{
  Task<MemorySearchResult> SearchAsync(
    MemoryQuery query,
    CancellationToken cancellationToken);
}
```

```json
{
  "ruleset": "wuhan-koukoufan",
  "turn": 8,
  "shanten": 1,
  "wildcard_count": 1,
  "open_count": 2,
  "opponent_patterns": ["opposite-pure-suit"],
  "candidate_discard": "9s",
  "text_query": "对家疑似清一色时如何兼顾牌效"
}
```

概念评分：

```text
score =
  domain_similarity
  semantic_similarity
  keyword_match
  relation_match
  temporal_validity
  quality_score
  - conflict_penalty
  - low_confidence_penalty
```

所有检索保存到 `retrieval_audit`，记录查询、候选、各分项得分和最终注入 Agent 的内容。

## 10. 向量与图存储路线

首版不部署 Qdrant、Milvus、Neo4j 或 Graphiti：

- Embedding 以浮点 BLOB 保存到 SQLite；
- 结构化 SQL 先将候选缩小；
- 小规模数据在 C# 中计算余弦相似度；
- 数据达到数万条并经过基准验证后再评估 `sqlite-vec`；
- 时序关系用 `memory_links` 和有效时间表达；
- 复杂图遍历成为真实瓶颈后再实现 Graphiti 适配器。

## 11. 自学习与后台整合

```csharp
public interface IMemoryConsolidator
{
  Task<IReadOnlyList<MemoryCandidate>> ConsolidateAsync(
    Episode episode,
    CancellationToken cancellationToken);
}

public interface IMemoryEvaluator
{
  Task<MemoryEvaluation> EvaluateAsync(
    MemoryCandidate candidate,
    CancellationToken cancellationToken);
}
```

`MemoryConsolidationService` 继承 `BackgroundService`，触发时机：

- 牌局结束；
- 程序空闲；
- 累积足够 Episode；
- 检测到记忆重复或冲突；
- 用户手动要求整理。

后台流程：

```text
Episode
→ 提取候选经验
→ 去重
→ 冲突检测
→ 规则验证
→ 策略回放
→ 固定评测集
→ candidate / confirmed / rejected
```

任何候选策略都先进入影子模式。只有固定评测指标提升且没有规则回归时，才能发布为新的策略版本。

## 12. 自动学习边界

允许自动形成候选：

- WindowProfile 视觉样本；
- 用户确认过的识别纠错；
- 对手行为统计；
- 用户解释偏好；
- 高频失败模式；
- 待验证的战术模式。

禁止自动晋升：

- 胡牌合法性；
- 癞子与计分规则；
- 基本动作规则；
- 未经固定评测的策略权重；
- 仅由一局输赢推导出的经验。

## 13. 策略准确性

### 硬约束层

目标为 100%：

- 合法动作；
- 胡牌判断；
- 癞子与二五八将；
- 开口和特殊牌限制；
- 牌数守恒。

### 数值策略层

- 向听数；
- 有效进张；
- 剩余有效牌；
- 收益估计；
- 下家吃牌风险；
- 对手危险度；
- 模拟期望收益。

### 经验补充层

- 相似牌局；
- 对手行为倾向；
- 解释案例；
- 个性化提示；
- 新策略候选。

经验层不能推翻硬约束层。

## 14. 提醒策略

- 用户回合：自动发布紧凑建议；
- 对手普通动作：仅更新低干扰信息条；
- 对手牌型倾向、疑似听牌、承包风险或下家吃牌风险显著变化：允许主动提醒；
- 对手信息只表述为倾向和风险，不表述为确定事实；
- 关键牌局状态不确定时暂停建议；
- 非关键不确定项可以保留建议，但明确显示影响范围。

## 15. 可靠性与恢复

- 每个 Provider 请求带超时、取消和有限重试；
- 相同稳定画面去重；
- Observation 使用幂等键；
- GameEvent 使用顺序号和幂等键；
- SQLite 采用 WAL；
- 每局定期生成状态快照；
- 后台整合任务可重试且幂等；
- 应用重启后恢复未结束牌局和未完成任务；
- 内存版本晋升支持回滚。

## 16. 可观测性

关键指标：

- 捕获采样率；
- 变化事件率；
- 多模态请求次数、延迟和成本；
- Observation 接受/拒绝率；
- GameState 矛盾率；
- 建议生成延迟；
- 规则与牌效测试通过率；
- 检索 Recall@K、MRR、命中来源；
- 候选记忆确认/拒绝率；
- 策略版本离线胜率和放铳风险指标。

不要把“捕获 FPS”和“多模态识别频率”混为一个指标。

## 17. 模块落位

```text
MahjongAgent.Core
  GameEvent、GameState、Episode、MemoryRecord

MahjongAgent.Agent
  AgentOrchestrator、状态机、工具调度、AlertPolicy

MahjongAgent.Perception
  多模态请求、Observation、置信度、Resolver

MahjongAgent.Rules.Wuhan
  武汉规则和合法性判断

MahjongAgent.Strategy
  牌效、危险度、模拟、DecisionSnapshot

MahjongAgent.Memory（新增）
  检索、整合、评测、冲突与版本管理

MahjongAgent.Storage
  SQLite、FTS5、事件、快照和记忆存储

MahjongAgent.Providers.OpenAICompatible（新增）
  多模态、文本和 Embedding Provider

MahjongAgent.Presentation（新增）
  跨平台只读 ViewModel 与 UI 数据契约

MahjongAgent.UI.Windows（新增）
  WPF Views、ResourceDictionary 和控件

MahjongAgent.Platform.Windows
  应用宿主、捕获、悬浮窗、托盘、密钥和 Win32
```

## 18. 实施阶段

### Phase 1

- GameEvent、GameState、Reducer；
- Observation Schema 和 Resolver；
- SQLite 事件存储与快照；
- 武汉基础规则和牌效测试。

### Phase 2

- Episode 与 DecisionSnapshot；
- FTS5 和结构化检索；
- Agent Orchestrator；
- 解释与追问 Provider。

### Phase 3

- MemoryCandidate；
- 后台 Consolidator；
- 冲突、版本与审核 UI；
- 固定牌局评测集。

### Phase 4

- Embedding 和混合检索；
- 对手倾向统计；
- 影子策略与版本晋升；
- 评估 `sqlite-vec` 或 Graphiti 适配器。

## 19. 参考设计来源

- [Mem0](https://github.com/mem0ai/mem0)：多信号检索、时间推理、实体关联；
- [Graphiti](https://github.com/getzep/graphiti)：时间有效期、关系和来源追踪；
- [LangGraph Memory](https://docs.langchain.com/oss/python/langgraph/memory)：短期/长期、语义/情景/程序记忆和后台写入；
- [Letta Memory](https://docs.letta.com/configuration/memory)：版本化记忆、后台整理和记忆体检；
- [Cognee](https://github.com/topoteretes/cognee)：remember/recall/forget/improve 生命周期。

这些项目是设计参考，不是首版运行时依赖。
