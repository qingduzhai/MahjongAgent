namespace MahjongAgent.Agent.Orchestration;

public sealed class AgentNodeRunner
{
  private readonly AgentOrchestrator _orchestrator;
  private readonly IReadOnlyDictionary<AgentPhase, IAgentNode> _nodes;

  public AgentNodeRunner(AgentOrchestrator orchestrator, IEnumerable<IAgentNode> nodes)
  {
    ArgumentNullException.ThrowIfNull(orchestrator);
    ArgumentNullException.ThrowIfNull(nodes);

    _orchestrator = orchestrator;
    var nodeMap = new Dictionary<AgentPhase, IAgentNode>();
    foreach (var node in nodes)
    {
      ArgumentNullException.ThrowIfNull(node);
      if (!IsExecutablePhase(node.Phase))
      {
        throw new ArgumentException(
          $"Phase {node.Phase} waits for external input and cannot have an executable node.",
          nameof(nodes));
      }

      if (!nodeMap.TryAdd(node.Phase, node))
      {
        throw new ArgumentException(
          $"More than one agent node was registered for phase {node.Phase}.",
          nameof(nodes));
      }
    }

    _nodes = nodeMap;
  }

  public async ValueTask<AgentTransition> RunCurrentAsync(
    CancellationToken cancellationToken = default)
  {
    var snapshot = _orchestrator.Current;
    if (!_nodes.TryGetValue(snapshot.Phase, out var node))
    {
      throw new InvalidOperationException(
        $"No executable agent node is registered for phase {snapshot.Phase}.");
    }

    var signal = await node.ExecuteAsync(snapshot, cancellationToken).ConfigureAwait(false);
    return await _orchestrator
      .DispatchAsync(signal, snapshot.Revision, cancellationToken)
      .ConfigureAwait(false);
  }

  private static bool IsExecutablePhase(AgentPhase phase) => phase is
    AgentPhase.Calibrating or
    AgentPhase.ResolvingObservation or
    AgentPhase.UpdatingState or
    AgentPhase.EvaluatingStrategy or
    AgentPhase.PublishingAdvice or
    AgentPhase.ConsolidatingMemory or
    AgentPhase.Recovering;
}
