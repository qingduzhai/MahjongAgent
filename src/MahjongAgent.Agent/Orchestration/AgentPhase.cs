namespace MahjongAgent.Agent.Orchestration;

public enum AgentPhase
{
  Idle = 0,
  Calibrating = 1,
  WaitingForRound = 2,
  Observing = 3,
  ResolvingObservation = 4,
  UpdatingState = 5,
  EvaluatingStrategy = 6,
  PublishingAdvice = 7,
  ConsolidatingMemory = 8,
  Paused = 9,
  Recovering = 10,
  Faulted = 11,
  Stopped = 12
}
