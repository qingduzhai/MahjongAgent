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
9. `IFrameSource` 只产生带时间戳的静态截图；游戏窗口、视频播放器或离线抽帧的来源差异不得进入牌局状态和策略核心。

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
IFrameSource（逐张静态截图）
  → FrameChanged
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

完整牌局视频可以通过播放器窗口实时截图，也可以由离线工具按时间戳抽成截图序列；Agent 和多模态 Provider 均不直接处理整段视频。截图中的 Observation 仍是候选，不因来源视频可重复播放而自动成为正确事实。训练、验证和测试数据必须按完整视频或牌局切分，禁止相邻截图泄漏。

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
  Task<PerceptionResult> ObserveAsync(
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

### 6.1 OpenAI-Compatible 首版协议

首个实现采用广泛兼容的 Chat Completions 多模态协议，不把运行时绑定到 OpenAI 自有端点：

```text
BaseUri + ChatCompletionsPath
  → POST chat/completions
  → messages[].content[text + image_url]
  → response_format
  → choices[0].message.content
```

配置只保存凭据引用，不保存真实 Key：

```json
{
  "providerId": "my-vision-provider",
  "baseUri": "https://provider.example/v1/",
  "chatCompletionsPath": "chat/completions",
  "perceptionModel": "vision-model",
  "credentialId": "mahjong-agent/provider/my-vision-provider",
  "apiKeyTransport": "Bearer",
  "structuredOutputMode": "JsonSchema"
}
```

认证传输支持：

- `Bearer`：`Authorization: Bearer <key>`；
- `Header`：可配置 Header 名称和前缀；
- `None`：用于本地兼容端点。

结构化输出支持两级：

1. `JsonSchema`：默认模式，发送严格 `response_format.json_schema`；
2. `JsonObject`：兼容降级模式，发送 `response_format: json_object`，并把 Schema 写入提示词。

不支持任何 JSON 保证的端点不进入牌局正式感知链路。即使 Provider 返回合法 JSON，也只能产生候选观察，仍需经过 Observation Schema、牌数守恒、时序和规则校验。

Provider 配置声明能力，构造时校验所选模式；端点连通性和真实能力探测将在设置页保存配置前执行。请求同时限制图片数量、单图大小、总字节数和超时。

能力探测使用内置的红蓝合成 PNG，不读取或上传游戏窗口。探测必须同时验证：

- Endpoint、鉴权和模型名称可用；
- 服务接受 `image_url` Data URI；
- 模型能识别合成图左右颜色，而不是忽略图片；
- 所选 `JsonSchema` 或 `JsonObject` 模式能返回可解析对象；
- 响应包含可记录的实际模型、耗时和请求 ID。

探测结果按配置错误、鉴权错误、限流、服务不可用、超时、协议错误和能力不符分类。上游错误正文截断并对当前 API Key 脱敏后才能进入诊断信息。能力探测由用户在设置页显式触发，可能产生一次极小的模型调用费用。

首版暂不实现 `/responses`、流式输出和厂商专用 SDK。未来可以增加协议模式，但不得改变上层 `IMultimodalPerceptionProvider` 契约。

### 6.2 Provider Profile 与凭据隔离

Provider Profile 通过 SQLite 编号迁移持久化，包含 Base URI、路径、模型、认证传输、结构化输出模式、图片 detail、非敏感 Header、能力声明和最近一次探测摘要。表中只保存 `credential_id`，没有 API Key 或 Secret 值字段。

```text
SQLite Provider Profile
  credential_id ───────┐
                       ▼
Windows Credential Manager
  MahjongAgent:<credential_id> → API Key
```

`WindowsCredentialManagerApiKeyStore` 使用当前 Windows 用户的 Generic Credential，持久级别为 Local Machine。Key 以 UTF-8 写入，受 WinCred 2560 字节上限约束；临时字节缓冲在读写后清零。测试默认使用内存替身，显式的 Win32 冒烟测试只写入固定合成测试项并在 `finally` 中删除。

SQLite 采用 `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlite3` 3.x，避免使用带已知高危公告的旧版原生 SQLite 传递依赖。启动时由 `SqliteMigrationRunner` 按资源编号执行迁移并记录到 `schema_migrations`。

### 6.3 测试并保存的补偿事务

SQLite 与 Windows Credential Manager 无法共享数据库事务，因此由 `OpenAiCompatibleProviderProfileService` 编排补偿事务：

```text
候选 Profile + 候选 Key（仅内存）
  → 合成图片能力探测
  → 失败：零持久化，返回分类诊断
  → 成功：生成 ProviderProbeSnapshot
  → 暂存新 Key 到 Credential Manager
  → Upsert SQLite Profile
  → 删除不再使用的旧 CredentialId
  → 成功
```

任一步失败时按相反方向补偿：恢复旧凭据、删除新凭据、恢复旧 Profile 或删除新 Profile。原始操作失败但补偿成功时保留原始异常；补偿也失败时抛出 `ProviderProfileTransactionException` 并附带全部补偿异常，禁止向 UI 假报成功。

约束：

- 候选 Key 在能力探测成功前不写入 Credential Manager；
- 探测 Runner 通过内存 Credential Resolver 使用候选 Key；
- 同一个 CredentialId 不允许分配给多个 Profile，避免删除时误伤；
- 切换为本地 `None` 认证后删除废弃 Key；
- 删除 Profile 时先删除独占 Key，SQLite 删除失败则恢复 Key；
- 调用取消发生在持久化中途时也执行不受取消令牌影响的补偿。

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
openai_compatible_provider_profiles
schema_migrations
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

- 固定截图序列、来源时间戳与可重复感知回归；
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
