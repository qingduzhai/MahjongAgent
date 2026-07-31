using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using MahjongAgent.Core.Gameplay;
using MahjongAgent.Core.Tiles;
using MahjongAgent.Perception.Observations;

namespace MahjongAgent.Perception.Resolution;

public sealed class ObservationResolver
{
  private readonly GameStateReducer reducer;
  private readonly TimeProvider timeProvider;
  private readonly Func<Guid> eventIdFactory;

  public ObservationResolver(
    GameStateReducer? reducer = null,
    TimeProvider? timeProvider = null,
    Func<Guid>? eventIdFactory = null)
  {
    this.reducer = reducer ?? new GameStateReducer();
    this.timeProvider = timeProvider ?? TimeProvider.System;
    this.eventIdFactory = eventIdFactory ?? Guid.NewGuid;
  }

  public ObservationResolutionResult Resolve(
    MahjongObservationBatch batch,
    ObservationResolutionContext context,
    ObservationResolutionOptions? options = null)
  {
    ArgumentNullException.ThrowIfNull(batch);
    ArgumentNullException.ThrowIfNull(context);
    options ??= new ObservationResolutionOptions();

    var state = context.GameState;
    var accepted = ImmutableArray.CreateBuilder<GameEvent>();
    var pending = ImmutableArray.CreateBuilder<ObservationResolutionIssue>();
    var ignored = ImmutableArray.CreateBuilder<ObservationResolutionIssue>();
    var rejected = ImmutableArray.CreateBuilder<ObservationResolutionIssue>();
    var confirmations = context.ConfirmationCounts.ToBuilder();
    var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);

    if (state.Phase is not GameSessionPhase.InProgress)
    {
      foreach (var observation in batch.Observations)
      {
        rejected.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          "No active game session can receive observation facts."));
      }

      return BuildResult(state, accepted, pending, ignored, rejected, confirmations);
    }

    foreach (var observation in batch.Observations)
    {
      if (!IsSupportedFact(observation))
      {
        ignored.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          "The observation is descriptive and has no v1 game-event mapping."));
        continue;
      }

      if (observation.Uncertain || observation.Candidates.Count != 1)
      {
        pending.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          "A game fact requires exactly one non-uncertain candidate."));
        continue;
      }

      var candidate = observation.Candidates[0];
      var effectiveConfidence = Math.Min(observation.Confidence, candidate.Confidence);
      if (effectiveConfidence < options.MinimumConfidence)
      {
        pending.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          $"Effective confidence {effectiveConfidence:F3} is below {options.MinimumConfidence:F3}."));
        continue;
      }

      var fingerprint = CreateFingerprint(observation, candidate.Value);
      if (!seenFingerprints.Add(fingerprint))
      {
        rejected.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          "The batch contains a duplicate fact fingerprint.",
          fingerprint));
        continue;
      }

      var confirmationCount = confirmations.TryGetValue(fingerprint, out var previousCount)
        ? previousCount + 1
        : 1;
      confirmations[fingerprint] = confirmationCount;
      if (confirmationCount < options.RequiredConfirmations)
      {
        pending.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          $"Waiting for {options.RequiredConfirmations} matching stable frames.",
          fingerprint,
          confirmationCount));
        continue;
      }

      var creation = TryCreateEvent(observation, candidate.Value, effectiveConfidence, state, context);
      if (creation.IgnoredReason is not null)
      {
        confirmations.Remove(fingerprint);
        ignored.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          creation.IgnoredReason,
          fingerprint,
          confirmationCount));
        continue;
      }

      if (creation.RejectionReason is not null || creation.Events.IsDefaultOrEmpty)
      {
        confirmations.Remove(fingerprint);
        rejected.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          creation.RejectionReason ?? "The observation could not produce a game event.",
          fingerprint,
          confirmationCount));
        continue;
      }

      try
      {
        var candidateState = state;
        foreach (var gameEvent in creation.Events)
        {
          candidateState = reducer.Apply(candidateState, gameEvent);
        }

        state = candidateState;
        accepted.AddRange(creation.Events);
        confirmations.Remove(fingerprint);
      }
      catch (GameStateTransitionException exception)
      {
        confirmations.Remove(fingerprint);
        rejected.Add(new ObservationResolutionIssue(
          observation.ObservationId,
          exception.Message,
          fingerprint,
          confirmationCount));
      }
    }

    return BuildResult(state, accepted, pending, ignored, rejected, confirmations);
  }

  private EventCreation TryCreateEvent(
    MahjongObservation observation,
    string candidateValue,
    double confidence,
    GameState state,
    ObservationResolutionContext context)
  {
    var provenance = new GameEventProvenance(
      GameEventSourceKind.Perception,
      confidence,
      new[] { observation.ObservationId }.Concat(observation.Evidence.FrameIds));
    var eventId = eventIdFactory();
    if (eventId == Guid.Empty)
    {
      return EventCreation.Rejected("The event ID provider returned an empty ID.");
    }

    var sequence = state.Revision + 1;
    var occurredAtUtc = timeProvider.GetUtcNow();

    if (observation.Entity is ObservationEntity.Tile && observation.Action is "discarded")
    {
      if (!TryGetSeat(observation.Region, context, out var seat, out var reason))
      {
        return EventCreation.Rejected(reason!);
      }

      if (!TileType.TryParse(candidateValue, out var tile))
      {
        return EventCreation.Rejected("Discard candidate is not a canonical tile code.");
      }

      var player = state.GetPlayer(seat);
      if (state.LastEventKind is GameEventKind.TileDiscarded &&
          state.LastActor == seat &&
          player.Discards.Length > 0 &&
          player.Discards[^1] == tile)
      {
        return EventCreation.Ignored("The same discard is already the latest accepted event.");
      }

      return EventCreation.Accepted(new TileDiscardedEvent(
        eventId,
        state.SessionId,
        sequence,
        occurredAtUtc,
        provenance,
        seat,
        tile));
    }

    if (observation.Entity is ObservationEntity.Tile && observation.Action is "drawn")
    {
      if (!TryGetSeat(observation.Region, context, out var seat, out var reason))
      {
        return EventCreation.Rejected(reason!);
      }

      if (seat is not PlayerSeat.Self)
      {
        return EventCreation.Rejected(
          "Opponent concealed tile identities cannot be promoted from visual hints.");
      }

      if (!TileType.TryParse(candidateValue, out var tile))
      {
        return EventCreation.Rejected("Draw candidate is not a canonical tile code.");
      }

      if (state.LastEventKind is GameEventKind.TileDrawn &&
          state.LastActor is PlayerSeat.Self &&
          state.GetPlayer(PlayerSeat.Self).KnownConcealedTiles.LastOrDefault() == tile)
      {
        return EventCreation.Ignored("The same draw is already the latest accepted event.");
      }

      return EventCreation.Accepted(new TileDrawnEvent(
        eventId,
        state.SessionId,
        sequence,
        occurredAtUtc,
        provenance,
        seat,
        tile));
    }

    if (observation.Entity is ObservationEntity.Indicator && observation.Action is "revealed")
    {
      if (!context.RegionIndicatorKinds.TryGetValue(observation.Region, out var indicatorKind))
      {
        return EventCreation.Rejected(
          $"Region '{observation.Region}' has no confirmed indicator mapping.");
      }

      if (!TileType.TryParse(candidateValue, out var tile))
      {
        return EventCreation.Rejected("Indicator candidate is not a canonical tile code.");
      }

      if (state.Indicators.Any(indicator =>
            indicator.IndicatorKind == indicatorKind && indicator.Tile == tile))
      {
        return EventCreation.Ignored("The indicator is already present in confirmed state.");
      }

      return EventCreation.Accepted(new IndicatorRevealedEvent(
        eventId,
        state.SessionId,
        sequence,
        occurredAtUtc,
        provenance,
        indicatorKind,
        tile));
    }

    if (observation.Entity is ObservationEntity.Turn && observation.Action is "current")
    {
      if (!PlayerSeatExtensions.TryParseProtocolValue(candidateValue, out var seat))
      {
        return EventCreation.Rejected(
          "Turn candidate must be self, next, opposite or previous.");
      }

      if (state.CurrentTurn == seat)
      {
        return EventCreation.Ignored("The current turn is already confirmed.");
      }

      var turnEvent = new TurnChangedEvent(
        eventId,
        state.SessionId,
        sequence,
        occurredAtUtc,
        provenance,
        seat);
      if (seat is PlayerSeat.Self || state.GetPlayer(seat).StructuralTileCount is not 13)
      {
        return EventCreation.Accepted(turnEvent);
      }

      var drawEventId = eventIdFactory();
      if (drawEventId == Guid.Empty)
      {
        return EventCreation.Rejected("The event ID provider returned an empty ID.");
      }

      return EventCreation.Accepted(
        turnEvent,
        new TileDrawnEvent(
          drawEventId,
          state.SessionId,
          sequence + 1,
          occurredAtUtc,
          provenance,
          seat,
          null));
    }

    return EventCreation.Ignored("The observation has no v1 game-event mapping.");
  }

  private static bool IsSupportedFact(MahjongObservation observation) =>
    (observation.Entity is ObservationEntity.Tile &&
     observation.Action is "discarded" or "drawn") ||
    (observation.Entity is ObservationEntity.Indicator && observation.Action is "revealed") ||
    (observation.Entity is ObservationEntity.Turn && observation.Action is "current");

  private static bool TryGetSeat(
    string region,
    ObservationResolutionContext context,
    out PlayerSeat seat,
    out string? reason)
  {
    if (context.RegionSeats.TryGetValue(region, out seat))
    {
      reason = null;
      return true;
    }

    reason = $"Region '{region}' has no confirmed player-seat mapping.";
    return false;
  }

  private static string CreateFingerprint(
    MahjongObservation observation,
    string candidateValue)
  {
    var canonical = string.Join(
      '\n',
      observation.Entity,
      observation.Action,
      observation.Region,
      candidateValue);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
      .ToLowerInvariant();
  }

  private static ObservationResolutionResult BuildResult(
    GameState state,
    ImmutableArray<GameEvent>.Builder accepted,
    ImmutableArray<ObservationResolutionIssue>.Builder pending,
    ImmutableArray<ObservationResolutionIssue>.Builder ignored,
    ImmutableArray<ObservationResolutionIssue>.Builder rejected,
    ImmutableDictionary<string, int>.Builder confirmations) =>
    new(
      state,
      accepted.ToImmutable(),
      pending.ToImmutable(),
      ignored.ToImmutable(),
      rejected.ToImmutable(),
      confirmations.ToImmutable());

  private sealed record EventCreation(
    ImmutableArray<GameEvent> Events,
    string? IgnoredReason,
    string? RejectionReason)
  {
    public static EventCreation Accepted(params GameEvent?[] gameEvents) => new(
      gameEvents.Where(gameEvent => gameEvent is not null).Cast<GameEvent>().ToImmutableArray(),
      null,
      null);

    public static EventCreation Ignored(string reason) => new([], reason, null);

    public static EventCreation Rejected(string reason) => new([], null, reason);
  }
}
