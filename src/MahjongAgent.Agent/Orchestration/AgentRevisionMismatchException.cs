namespace MahjongAgent.Agent.Orchestration;

public sealed class AgentRevisionMismatchException : InvalidOperationException
{
  public AgentRevisionMismatchException(long expectedRevision, long actualRevision)
    : base($"Agent revision changed from {expectedRevision} to {actualRevision} before the node completed.")
  {
    ExpectedRevision = expectedRevision;
    ActualRevision = actualRevision;
  }

  public long ExpectedRevision { get; }

  public long ActualRevision { get; }
}
