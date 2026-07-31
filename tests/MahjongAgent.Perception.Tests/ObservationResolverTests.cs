using MahjongAgent.Core.Gameplay;
using MahjongAgent.Core.Tiles;
using MahjongAgent.Perception.Observations;
using MahjongAgent.Perception.Prompting;
using MahjongAgent.Perception.Resolution;

namespace MahjongAgent.Perception.Tests;

public sealed class ObservationResolverTests
{
  private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
  private static readonly DateTimeOffset Now =
    new(2026, 7, 31, 2, 3, 4, TimeSpan.Zero);
  private readonly GameStateReducer reducer = new();
  private int eventCounter;

  [Fact]
  public void Resolve_requires_two_matching_frames_before_promoting_a_discard()
  {
    var state = ActiveState(PlayerSeat.Next);
    var first = CreateResolver().Resolve(
      Batch(Observation("obs-1", ObservationEntity.Tile, "discarded", "next_discard", "9s")),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 2));

    var pending = Assert.Single(first.Pending);
    Assert.Equal(1, pending.ConfirmationCount);
    Assert.Empty(first.AcceptedEvents);
    Assert.Equal(state.Revision, first.GameState.Revision);

    var second = CreateResolver().Resolve(
      Batch(Observation("obs-2", ObservationEntity.Tile, "discarded", "next_discard", "9s")),
      Context(state, first.ConfirmationCounts),
      new ObservationResolutionOptions(requiredConfirmations: 2));

    var accepted = Assert.IsType<TileDiscardedEvent>(Assert.Single(second.AcceptedEvents));
    Assert.Equal(PlayerSeat.Next, accepted.Player);
    Assert.Equal("9s", accepted.Tile.Code);
    Assert.Equal(state.Revision + 1, second.GameState.Revision);
    Assert.Empty(second.ConfirmationCounts);
  }

  [Fact]
  public void Resolve_keeps_low_confidence_and_ambiguous_observations_pending()
  {
    var state = ActiveState(PlayerSeat.Next);
    var lowConfidence = Observation(
      "obs-low",
      ObservationEntity.Tile,
      "discarded",
      "next_discard",
      "9s",
      confidence: 0.7);
    var ambiguous = Observation(
      "obs-ambiguous",
      ObservationEntity.Tile,
      "discarded",
      "next_discard",
      "9s") with
    {
      Uncertain = true,
      Candidates =
      [
        new ObservationCandidate { Value = "9s", Confidence = 0.95 },
        new ObservationCandidate { Value = "6s", Confidence = 0.91 }
      ]
    };

    var result = CreateResolver().Resolve(
      Batch(lowConfidence, ambiguous),
      Context(state),
      new ObservationResolutionOptions(minimumConfidence: 0.9, requiredConfirmations: 1));

    Assert.Equal(2, result.Pending.Length);
    Assert.Empty(result.AcceptedEvents);
    Assert.Empty(result.ConfirmationCounts);
  }

  [Fact]
  public void Resolve_does_not_treat_duplicate_observations_in_one_frame_as_two_confirmations()
  {
    var state = ActiveState(PlayerSeat.Next);
    var result = CreateResolver().Resolve(
      Batch(
        Observation("obs-1", ObservationEntity.Tile, "discarded", "next_discard", "9s"),
        Observation("obs-2", ObservationEntity.Tile, "discarded", "next_discard", "9s")),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 2));

    Assert.Single(result.Pending);
    Assert.Contains("duplicate fact fingerprint", Assert.Single(result.Rejected).Reason);
    Assert.Empty(result.AcceptedEvents);
    Assert.Equal(1, Assert.Single(result.ConfirmationCounts).Value);
  }

  [Fact]
  public void Resolve_ignores_descriptive_observations_without_mutating_state()
  {
    var state = ActiveState(PlayerSeat.Self);
    var result = CreateResolver().Resolve(
      Batch(Observation(
        "obs-layout",
        ObservationEntity.LayoutRegion,
        "visible",
        "whole_table",
        "four-player-layout")),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 1));

    Assert.Single(result.Ignored);
    Assert.Empty(result.AcceptedEvents);
    Assert.Same(state, result.GameState);
  }

  [Fact]
  public void Resolve_rejects_fact_from_unmapped_region()
  {
    var state = ActiveState(PlayerSeat.Next);
    var result = CreateResolver().Resolve(
      Batch(Observation("obs-1", ObservationEntity.Tile, "discarded", "mystery", "9s")),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 1));

    Assert.Contains("no confirmed player-seat mapping", Assert.Single(result.Rejected).Reason);
    Assert.Empty(result.AcceptedEvents);
  }

  [Fact]
  public void Resolve_uses_reducer_to_reject_discard_missing_from_known_self_hand()
  {
    var state = ActiveState(PlayerSeat.Self);
    state = reducer.Apply(
      state,
      new SelfHandSnapshotConfirmedEvent(
        EventId(),
        SessionId,
        2,
        Now,
        GameEventProvenance.System,
        InitialDealerHand().Select(TileType.Parse),
        0));

    var result = CreateResolver().Resolve(
      Batch(Observation("obs-1", ObservationEntity.Tile, "discarded", "self_hand", "9s")),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 1));

    Assert.Contains("does not contain tile", Assert.Single(result.Rejected).Reason);
    Assert.Empty(result.AcceptedEvents);
    Assert.Equal(2, result.GameState.Revision);
  }

  [Fact]
  public void Resolve_ignores_duplicate_latest_discard()
  {
    var state = ActiveState(PlayerSeat.Next);
    state = reducer.Apply(
      state,
      new TileDiscardedEvent(
        EventId(),
        SessionId,
        2,
        Now,
        GameEventProvenance.System,
        PlayerSeat.Next,
        TileType.Parse("9s")));

    var result = CreateResolver().Resolve(
      Batch(Observation("obs-repeat", ObservationEntity.Tile, "discarded", "next_discard", "9s")),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 1));

    Assert.Contains("already the latest accepted event", Assert.Single(result.Ignored).Reason);
    Assert.Empty(result.AcceptedEvents);
    Assert.Equal(2, result.GameState.Revision);
  }

  [Fact]
  public void Resolve_promotes_turn_and_indicator_with_confirmed_mappings()
  {
    var state = ActiveState(PlayerSeat.Self);
    var turn = Observation("obs-turn", ObservationEntity.Turn, "current", "turn_marker", "next");
    var indicator = Observation(
      "obs-indicator",
      ObservationEntity.Indicator,
      "revealed",
      "wildcard_indicator",
      "5p");

    var result = CreateResolver().Resolve(
      Batch(turn, indicator),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 1));

    Assert.Collection(
      result.AcceptedEvents,
      gameEvent => Assert.IsType<TurnChangedEvent>(gameEvent),
      gameEvent =>
      {
        var draw = Assert.IsType<TileDrawnEvent>(gameEvent);
        Assert.Equal(PlayerSeat.Next, draw.Player);
        Assert.Null(draw.Tile);
      },
      gameEvent => Assert.IsType<IndicatorRevealedEvent>(gameEvent));
    Assert.Equal(PlayerSeat.Next, result.GameState.CurrentTurn);
    Assert.Equal(14, result.GameState.GetPlayer(PlayerSeat.Next).ConcealedTileCount);
    Assert.Equal("5p", Assert.Single(result.GameState.Indicators).Tile.Code);
    Assert.Equal(4, result.GameState.Revision);
  }

  [Fact]
  public void Resolve_never_promotes_an_opponent_hidden_draw_identity()
  {
    var state = ActiveState(PlayerSeat.Next);
    var result = CreateResolver().Resolve(
      Batch(Observation("obs-draw", ObservationEntity.Tile, "drawn", "next_discard", "5p")),
      Context(state),
      new ObservationResolutionOptions(requiredConfirmations: 1));

    Assert.Contains("Opponent concealed tile identities", Assert.Single(result.Rejected).Reason);
    Assert.Empty(result.AcceptedEvents);
  }

  private ObservationResolver CreateResolver() => new(
    reducer,
    new FixedTimeProvider(Now),
    EventId);

  private ObservationResolutionContext Context(
    GameState state,
    IEnumerable<KeyValuePair<string, int>>? confirmations = null) => new(
    state,
    new Dictionary<string, PlayerSeat>
    {
      ["self_hand"] = PlayerSeat.Self,
      ["next_discard"] = PlayerSeat.Next,
      ["opposite_discard"] = PlayerSeat.Opposite,
      ["previous_discard"] = PlayerSeat.Previous
    },
    new Dictionary<string, IndicatorKind>
    {
      ["wildcard_indicator"] = IndicatorKind.Wildcard,
      ["skin_indicator"] = IndicatorKind.Skin
    },
    confirmations);

  private GameState ActiveState(PlayerSeat dealer) => reducer.Apply(
    GameState.Empty,
    new RoundStartedEvent(
      EventId(),
      SessionId,
      1,
      Now,
      GameEventProvenance.System,
      "wuhan-preview-v1",
      dealer));

  private static MahjongObservationBatch Batch(params MahjongObservation[] observations) => new()
  {
    SchemaVersion = MahjongObservationContract.SchemaVersion,
    Mode = PerceptionMode.Observe,
    FrameIds = ["frame-1"],
    Observations = observations,
    Uncertainties = []
  };

  private static MahjongObservation Observation(
    string id,
    ObservationEntity entity,
    string action,
    string region,
    string value,
    double confidence = 0.98) => new()
    {
      ObservationId = id,
      Entity = entity,
      Action = action,
      Region = region,
      Candidates = [new ObservationCandidate { Value = value, Confidence = confidence }],
      Confidence = confidence,
      Uncertain = false,
      Evidence = new ObservationEvidence
      {
        FrameIds = ["frame-1"],
        VisualCues = ["stable visible cue"]
      }
    };

  private Guid EventId() => Guid.Parse($"00000000-0000-0000-0000-{++eventCounter:000000000000}");

  private static string[] InitialDealerHand() =>
    ["1m", "2m", "3m", "4m", "5m", "6m", "7m", "8m", "9m", "1p", "2p", "3p", "4p", "5p"];

  private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => value;
  }
}
