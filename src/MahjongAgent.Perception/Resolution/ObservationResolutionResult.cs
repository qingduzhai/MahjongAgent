using System.Collections.Immutable;
using MahjongAgent.Core.Gameplay;

namespace MahjongAgent.Perception.Resolution;

public sealed record ObservationResolutionIssue(
  string ObservationId,
  string Reason,
  string? Fingerprint = null,
  int ConfirmationCount = 0);

public sealed record ObservationResolutionResult(
  GameState GameState,
  ImmutableArray<GameEvent> AcceptedEvents,
  ImmutableArray<ObservationResolutionIssue> Pending,
  ImmutableArray<ObservationResolutionIssue> Ignored,
  ImmutableArray<ObservationResolutionIssue> Rejected,
  ImmutableDictionary<string, int> ConfirmationCounts);
