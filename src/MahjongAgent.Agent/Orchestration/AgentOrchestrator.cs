namespace MahjongAgent.Agent.Orchestration;

public sealed class AgentOrchestrator
{
  private readonly IAgentCheckpointStore _checkpointStore;
  private readonly SemaphoreSlim _dispatchLock = new(1, 1);
  private AgentWorkflowSnapshot _current;

  private AgentOrchestrator(
    IAgentCheckpointStore checkpointStore,
    AgentWorkflowSnapshot current)
  {
    _checkpointStore = checkpointStore;
    _current = current;
  }

  public AgentWorkflowSnapshot Current => Volatile.Read(ref _current);

  public static async ValueTask<AgentOrchestrator> CreateAsync(
    IAgentCheckpointStore? checkpointStore = null,
    CancellationToken cancellationToken = default)
  {
    var store = checkpointStore ?? new NoOpAgentCheckpointStore();
    var current = await store.LoadAsync(cancellationToken).ConfigureAwait(false) ??
      AgentWorkflowSnapshot.Initial;
    AgentStateMachine.ValidateSnapshot(current);

    return new AgentOrchestrator(store, current);
  }

  public async ValueTask<AgentTransition> DispatchAsync(
    AgentSignal signal,
    CancellationToken cancellationToken = default)
    => await DispatchCoreAsync(signal, null, cancellationToken).ConfigureAwait(false);

  public async ValueTask<AgentTransition> DispatchAsync(
    AgentSignal signal,
    long expectedRevision,
    CancellationToken cancellationToken = default)
    => await DispatchCoreAsync(signal, expectedRevision, cancellationToken).ConfigureAwait(false);

  private async ValueTask<AgentTransition> DispatchCoreAsync(
    AgentSignal signal,
    long? expectedRevision,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(signal);
    if (expectedRevision < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(expectedRevision));
    }

    await _dispatchLock.WaitAsync(cancellationToken).ConfigureAwait(false);

    try
    {
      if (expectedRevision is { } revision && revision != _current.Revision)
      {
        throw new AgentRevisionMismatchException(revision, _current.Revision);
      }

      var transition = AgentStateMachine.Apply(_current, signal);
      await _checkpointStore
        .SaveAsync(transition.Current, cancellationToken)
        .ConfigureAwait(false);
      Volatile.Write(ref _current, transition.Current);
      return transition;
    }
    finally
    {
      _dispatchLock.Release();
    }
  }
}
