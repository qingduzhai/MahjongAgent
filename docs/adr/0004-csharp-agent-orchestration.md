# ADR-0004：C# 强类型 Agent 编排，不以 LangGraph 为核心运行时

- 状态：Accepted
- 日期：2026-07-30

## 背景

MahjongAgent 需要连续处理截图变化、候选观察、规则校验、牌局状态、策略计算、提示发布和长期记忆。这个流程看起来像 Agent Graph，但其中绝大多数分支由确定性的领域状态决定，而不是由语言模型自由选择。

桌面程序的主运行时是 .NET 10/WPF。将核心编排放到 LangGraph 会引入 Python 或 JavaScript 进程、跨进程序列化、第二套部署与日志链路，并使截图缓冲、取消、窗口生命周期和 SQLite 事务跨越运行时边界。

## 决策

1. 首版使用 `MahjongAgent.Agent` 内的 C# 强类型状态机和 `AgentOrchestrator`。
2. `AgentSignal → AgentStateMachine → AgentWorkflowSnapshot` 是唯一合法状态迁移入口；不存在由提示词隐式决定的流程跳转。
3. 每次迁移先保存 `IAgentCheckpointStore`，成功后才发布内存状态，避免出现未持久化但 UI 已显示的新状态。
4. 多模态模型、规则、策略、记忆检索和解释器实现为 `IAgentNode`，由 `AgentNodeRunner` 执行；模型可以产生候选内容，但不能改变图结构、跳过验证或直接写入正式牌局事实。
5. 暂停、恢复、可恢复故障、致命故障、停止和重置都是显式信号并接受同一套迁移校验。
6. 节点开始时记录 snapshot revision，结束时使用乐观并发检查提交；节点运行期间若发生暂停、停止或其他外部迁移，旧结果必须拒绝。
7. LangGraph 的 checkpoint、短期/长期记忆分层和后台整理思想继续作为设计参考，但不是产品运行时依赖。

## 首版状态图

```text
Idle
  → Calibrating? → WaitingForRound
  → Observing
  → ResolvingObservation
  → UpdatingState?
  → EvaluatingStrategy?
  → PublishingAdvice?
  → Observing
  → ConsolidatingMemory → WaitingForRound

任一活动阶段 → Paused → 原阶段
任一活动阶段 → Recovering → 原阶段
任一非停止阶段 → Faulted 或 Stopped
Faulted/Stopped → Reset → Idle
```

## 何时重新评估 LangGraph

只有同时出现以下证据之一时才做适配器实验，而不是直接替换核心：

- 出现真正动态、运行前无法枚举的多 Agent 协作图；
- 已经存在必须复用的 Python Agent/训练服务，跨进程成本不再是新增成本；
- C# 状态机在固定评测集上明确限制了任务完成率，而问题无法通过增加强类型节点解决；
- LangGraph 适配器通过崩溃恢复、延迟、内存、安装体积和可观测性基准。

即使以后接入，`GameEvent`、`GameState`、规则校验和策略引擎仍保留在 C# 领域核心，LangGraph 只能作为外层工作流适配器。

## 结果

正面结果：单进程部署、编译期契约、确定性回放、低延迟、简单取消语义和可测试的故障恢复。

代价：需要自行维护少量状态机和 checkpoint 代码；未来复杂动态图能力不会自动获得，需要用评测证明后再引入。
