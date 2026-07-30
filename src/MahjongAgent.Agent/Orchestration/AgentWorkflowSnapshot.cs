namespace MahjongAgent.Agent.Orchestration;

public sealed record AgentWorkflowSnapshot
{
  public long Revision { get; init; }

  public AgentPhase Phase { get; init; } = AgentPhase.Idle;

  public string? SourceId { get; init; }

  public string? SessionId { get; init; }

  public long? LastFrameSequenceNumber { get; init; }

  public AgentPhase? ResumePhase { get; init; }

  public AgentFailure? LastFailure { get; init; }

  public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

  public static AgentWorkflowSnapshot Initial { get; } = new();
}
