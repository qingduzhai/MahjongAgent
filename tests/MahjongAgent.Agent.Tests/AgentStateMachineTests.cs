using MahjongAgent.Agent.Orchestration;

namespace MahjongAgent.Agent.Tests;

public sealed class AgentStateMachineTests
{
  private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void HappyPathMakesEveryDecisionBoundaryExplicit()
  {
    var snapshot = AgentWorkflowSnapshot.Initial;

    snapshot = Apply(snapshot, new TargetAttachedSignal("window:42", "session:1", false, Now));
    Assert.Equal(AgentPhase.WaitingForRound, snapshot.Phase);

    snapshot = Apply(snapshot, new RoundStartedSignal(Now.AddSeconds(1)));
    snapshot = Apply(snapshot, new FrameChangedSignal(1, Now.AddSeconds(2)));
    snapshot = Apply(snapshot, new ObservationResolvedSignal(0, Now.AddSeconds(3)));
    Assert.Equal(AgentPhase.Observing, snapshot.Phase);

    snapshot = Apply(snapshot, new FrameChangedSignal(2, Now.AddSeconds(4)));
    Assert.Equal(2, snapshot.LastFrameSequenceNumber);
    snapshot = Apply(snapshot, new ObservationResolvedSignal(1, Now.AddSeconds(5)));
    snapshot = Apply(snapshot, new StateUpdatedSignal(true, Now.AddSeconds(6)));
    snapshot = Apply(snapshot, new StrategyEvaluatedSignal(true, Now.AddSeconds(7)));
    Assert.Equal(AgentPhase.PublishingAdvice, snapshot.Phase);

    snapshot = Apply(snapshot, new AdvicePublishedSignal(Now.AddSeconds(8)));
    snapshot = Apply(snapshot, new RoundEndedSignal(Now.AddSeconds(9)));
    snapshot = Apply(snapshot, new MemoryConsolidatedSignal(Now.AddSeconds(10)));

    Assert.Equal(AgentPhase.WaitingForRound, snapshot.Phase);
    Assert.Equal(11, snapshot.Revision);
  }

  [Fact]
  public void CalibrationAndPauseResumeReturnToTheInterruptedPhase()
  {
    var snapshot = Apply(
      AgentWorkflowSnapshot.Initial,
      new TargetAttachedSignal("window:42", "session:1", true, Now));
    Assert.Equal(AgentPhase.Calibrating, snapshot.Phase);

    snapshot = Apply(snapshot, new PauseRequestedSignal(Now.AddSeconds(1)));
    Assert.Equal(AgentPhase.Paused, snapshot.Phase);
    Assert.Equal(AgentPhase.Calibrating, snapshot.ResumePhase);

    snapshot = Apply(snapshot, new ResumeRequestedSignal(Now.AddSeconds(2)));
    snapshot = Apply(snapshot, new CalibrationCompletedSignal(Now.AddSeconds(3)));

    Assert.Equal(AgentPhase.WaitingForRound, snapshot.Phase);
    Assert.Null(snapshot.ResumePhase);
  }

  [Fact]
  public void RecoveryReturnsToTheFailedPhaseAndClearsFailure()
  {
    var snapshot = ObservingSnapshot();
    snapshot = Apply(snapshot, new FrameChangedSignal(1, Now.AddSeconds(2)));
    snapshot = Apply(
      snapshot,
      new RecoverableFailureSignal("provider.timeout", "Timed out", Now.AddSeconds(3)));

    Assert.Equal(AgentPhase.Recovering, snapshot.Phase);
    Assert.Equal(AgentPhase.ResolvingObservation, snapshot.ResumePhase);
    Assert.True(snapshot.LastFailure?.IsRecoverable);

    snapshot = Apply(snapshot, new RecoveryCompletedSignal(Now.AddSeconds(4)));

    Assert.Equal(AgentPhase.ResolvingObservation, snapshot.Phase);
    Assert.Null(snapshot.ResumePhase);
    Assert.Null(snapshot.LastFailure);
  }

  [Fact]
  public void InvalidSignalDoesNotCreateAnImplicitTransition()
  {
    Assert.Throws<AgentTransitionException>(() => AgentStateMachine.Apply(
      AgentWorkflowSnapshot.Initial,
      new FrameChangedSignal(1, Now)));
  }

  private static AgentWorkflowSnapshot ObservingSnapshot()
  {
    var snapshot = Apply(
      AgentWorkflowSnapshot.Initial,
      new TargetAttachedSignal("window:42", "session:1", false, Now));
    return Apply(snapshot, new RoundStartedSignal(Now.AddSeconds(1)));
  }

  private static AgentWorkflowSnapshot Apply(AgentWorkflowSnapshot snapshot, AgentSignal signal) =>
    AgentStateMachine.Apply(snapshot, signal).Current;
}
