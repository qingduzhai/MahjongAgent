namespace MahjongAgent.Agent.Orchestration;

public sealed class AgentTransitionException : InvalidOperationException
{
  public AgentTransitionException(AgentPhase phase, Type signalType)
    : base($"Signal {signalType.Name} is not valid while the agent is in phase {phase}.")
  {
    Phase = phase;
    SignalType = signalType;
  }

  public AgentPhase Phase { get; }

  public Type SignalType { get; }
}
