using MahjongAgent.Agent.Orchestration;

namespace MahjongAgent.Agent.Tests;

public sealed class AgentOrchestratorTests
{
  private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public async Task DispatchPersistsCheckpointBeforePublishingState()
  {
    var store = new RecordingCheckpointStore();
    var orchestrator = await AgentOrchestrator.CreateAsync(store);

    var transition = await orchestrator.DispatchAsync(
      new TargetAttachedSignal("window:42", "session:1", false, Now));

    Assert.Equal(AgentPhase.WaitingForRound, transition.Current.Phase);
    Assert.Same(transition.Current, orchestrator.Current);
    Assert.Same(transition.Current, store.Saved);
  }

  [Fact]
  public async Task FailedCheckpointDoesNotPublishUncommittedState()
  {
    var store = new RecordingCheckpointStore { FailSave = true };
    var orchestrator = await AgentOrchestrator.CreateAsync(store);

    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
      await orchestrator.DispatchAsync(
        new TargetAttachedSignal("window:42", "session:1", false, Now)));

    Assert.Equal(AgentWorkflowSnapshot.Initial, orchestrator.Current);
  }

  [Fact]
  public async Task CreateRestoresLastCheckpoint()
  {
    var checkpoint = AgentWorkflowSnapshot.Initial with
    {
      Revision = 8,
      Phase = AgentPhase.Observing,
      SourceId = "window:42",
      SessionId = "session:1",
      UpdatedAtUtc = Now
    };
    var store = new RecordingCheckpointStore { Loaded = checkpoint };

    var orchestrator = await AgentOrchestrator.CreateAsync(store);

    Assert.Same(checkpoint, orchestrator.Current);
  }

  [Fact]
  public async Task CreateRejectsCorruptCheckpoint()
  {
    var checkpoint = AgentWorkflowSnapshot.Initial with
    {
      Revision = 8,
      Phase = AgentPhase.Observing,
      UpdatedAtUtc = Now
    };
    var store = new RecordingCheckpointStore { Loaded = checkpoint };

    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await AgentOrchestrator.CreateAsync(store));
  }

  private sealed class RecordingCheckpointStore : IAgentCheckpointStore
  {
    public AgentWorkflowSnapshot? Loaded { get; init; }

    public AgentWorkflowSnapshot? Saved { get; private set; }

    public bool FailSave { get; init; }

    public ValueTask<AgentWorkflowSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return ValueTask.FromResult(Loaded);
    }

    public ValueTask SaveAsync(
      AgentWorkflowSnapshot snapshot,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (FailSave)
      {
        throw new InvalidOperationException("Checkpoint failed.");
      }

      Saved = snapshot;
      return ValueTask.CompletedTask;
    }
  }
}
