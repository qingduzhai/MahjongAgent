namespace MahjongAgent.Agent.Orchestration;

public abstract record AgentSignal(DateTimeOffset OccurredAtUtc);

public sealed record TargetAttachedSignal(
  string SourceId,
  string SessionId,
  bool RequiresCalibration,
  DateTimeOffset OccurredAtUtc) : AgentSignal(OccurredAtUtc);

public sealed record CalibrationCompletedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record RoundStartedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record FrameChangedSignal(long SequenceNumber, DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record ObservationResolvedSignal(int AcceptedEventCount, DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record StateUpdatedSignal(bool RequiresDecision, DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record StrategyEvaluatedSignal(bool ShouldPublish, DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record AdvicePublishedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record RoundEndedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record MemoryConsolidatedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record PauseRequestedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record ResumeRequestedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record RecoverableFailureSignal(
  string Code,
  string Message,
  DateTimeOffset OccurredAtUtc) : AgentSignal(OccurredAtUtc);

public sealed record RecoveryCompletedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record FatalFailureSignal(
  string Code,
  string Message,
  DateTimeOffset OccurredAtUtc) : AgentSignal(OccurredAtUtc);

public sealed record StopRequestedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);

public sealed record ResetRequestedSignal(DateTimeOffset OccurredAtUtc)
  : AgentSignal(OccurredAtUtc);
