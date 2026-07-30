namespace MahjongAgent.Agent.Orchestration;

public sealed record AgentTransition(
  AgentWorkflowSnapshot Previous,
  AgentWorkflowSnapshot Current,
  AgentSignal Signal);
