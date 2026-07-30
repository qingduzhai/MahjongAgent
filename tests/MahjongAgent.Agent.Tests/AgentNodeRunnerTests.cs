using MahjongAgent.Agent.Orchestration;

namespace MahjongAgent.Agent.Tests;

public sealed class AgentNodeRunnerTests
{
  private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public async Task NodeResultIsRoutedThroughTheStateMachine()
  {
    var orchestrator = await CreateCalibratingOrchestratorAsync();
    var node = new StubNode(
      AgentPhase.Calibrating,
      new CalibrationCompletedSignal(Now.AddSeconds(1)));
    var runner = new AgentNodeRunner(orchestrator, [node]);

    var transition = await runner.RunCurrentAsync();

    Assert.Equal(AgentPhase.WaitingForRound, transition.Current.Phase);
    Assert.Equal(1, node.ExecutionCount);
  }

  [Fact]
  public async Task NodeCannotBypassALegalTransition()
  {
    var orchestrator = await CreateCalibratingOrchestratorAsync();
    var node = new StubNode(
      AgentPhase.Calibrating,
      new AdvicePublishedSignal(Now.AddSeconds(1)));
    var runner = new AgentNodeRunner(orchestrator, [node]);

    await Assert.ThrowsAsync<AgentTransitionException>(async () =>
      await runner.RunCurrentAsync());

    Assert.Equal(AgentPhase.Calibrating, orchestrator.Current.Phase);
  }

  [Fact]
  public async Task StaleNodeResultIsRejectedAfterExternalStateChange()
  {
    var orchestrator = await CreateCalibratingOrchestratorAsync();
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var node = new BlockingNode(completion.Task);
    var runner = new AgentNodeRunner(orchestrator, [node]);
    var runTask = runner.RunCurrentAsync().AsTask();

    await node.Started.Task;
    await orchestrator.DispatchAsync(new PauseRequestedSignal(Now.AddSeconds(1)));
    completion.SetResult();

    await Assert.ThrowsAsync<AgentRevisionMismatchException>(async () => await runTask);
    Assert.Equal(AgentPhase.Paused, orchestrator.Current.Phase);
  }

  [Fact]
  public async Task ExternalWaitPhaseCannotRegisterExecutableNode()
  {
    var orchestrator = await AgentOrchestrator.CreateAsync();
    var node = new StubNode(AgentPhase.Observing, new StopRequestedSignal(Now));

    Assert.Throws<ArgumentException>(() => new AgentNodeRunner(orchestrator, [node]));
  }

  private static async Task<AgentOrchestrator> CreateCalibratingOrchestratorAsync()
  {
    var orchestrator = await AgentOrchestrator.CreateAsync();
    await orchestrator.DispatchAsync(
      new TargetAttachedSignal("window:42", "session:1", true, Now));
    return orchestrator;
  }

  private sealed class StubNode(AgentPhase phase, AgentSignal result) : IAgentNode
  {
    public AgentPhase Phase { get; } = phase;

    public int ExecutionCount { get; private set; }

    public ValueTask<AgentSignal> ExecuteAsync(
      AgentWorkflowSnapshot snapshot,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ExecutionCount++;
      return ValueTask.FromResult(result);
    }
  }

  private sealed class BlockingNode(Task completion) : IAgentNode
  {
    public AgentPhase Phase => AgentPhase.Calibrating;

    public TaskCompletionSource Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<AgentSignal> ExecuteAsync(
      AgentWorkflowSnapshot snapshot,
      CancellationToken cancellationToken = default)
    {
      Started.SetResult();
      await completion.WaitAsync(cancellationToken);
      return new CalibrationCompletedSignal(Now.AddSeconds(2));
    }
  }
}
