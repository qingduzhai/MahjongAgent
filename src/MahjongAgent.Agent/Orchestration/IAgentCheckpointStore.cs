namespace MahjongAgent.Agent.Orchestration;

public interface IAgentCheckpointStore
{
  ValueTask<AgentWorkflowSnapshot?> LoadAsync(CancellationToken cancellationToken = default);

  ValueTask SaveAsync(
    AgentWorkflowSnapshot snapshot,
    CancellationToken cancellationToken = default);
}
