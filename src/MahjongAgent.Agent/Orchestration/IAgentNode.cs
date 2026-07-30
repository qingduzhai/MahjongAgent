namespace MahjongAgent.Agent.Orchestration;

public interface IAgentNode
{
  AgentPhase Phase { get; }

  ValueTask<AgentSignal> ExecuteAsync(
    AgentWorkflowSnapshot snapshot,
    CancellationToken cancellationToken = default);
}
