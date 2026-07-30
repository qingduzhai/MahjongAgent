namespace MahjongAgent.Agent.Orchestration;

public sealed class NoOpAgentCheckpointStore : IAgentCheckpointStore
{
  public ValueTask<AgentWorkflowSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    return ValueTask.FromResult<AgentWorkflowSnapshot?>(null);
  }

  public ValueTask SaveAsync(
    AgentWorkflowSnapshot snapshot,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(snapshot);
    cancellationToken.ThrowIfCancellationRequested();
    return ValueTask.CompletedTask;
  }
}
