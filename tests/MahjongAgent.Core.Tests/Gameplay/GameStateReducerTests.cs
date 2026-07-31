using MahjongAgent.Core.Gameplay;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Tests.Gameplay;

public sealed class GameStateReducerTests
{
  private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
  private static readonly DateTimeOffset OccurredAtUtc =
    new(2026, 7, 31, 1, 2, 3, TimeSpan.Zero);
  private readonly GameStateReducer reducer = new();
  private int eventCounter;

  [Fact]
  public void Replay_rebuilds_round_state_from_ordered_events()
  {
    var events = new GameEvent[]
    {
      Started(1, PlayerSeat.Self),
      SelfHand(2, InitialDealerHand()),
      Discarded(3, PlayerSeat.Self, "5p"),
      TurnChanged(4, PlayerSeat.Next),
      Drawn(5, PlayerSeat.Next),
      Discarded(6, PlayerSeat.Next, "9s")
    };

    var first = reducer.Replay(events);
    var second = reducer.Replay(events);

    Assert.Equal(6, first.Revision);
    Assert.Equal(GameSessionPhase.InProgress, first.Phase);
    Assert.Equal(PlayerSeat.Next, first.CurrentTurn);
    Assert.Equal(13, first.GetPlayer(PlayerSeat.Self).ConcealedTileCount);
    Assert.Equal(13, first.GetPlayer(PlayerSeat.Next).ConcealedTileCount);
    Assert.Equal("5p", Assert.Single(first.GetPlayer(PlayerSeat.Self).Discards).Code);
    Assert.Equal("9s", Assert.Single(first.GetPlayer(PlayerSeat.Next).Discards).Code);
    Assert.Equal(first.Revision, second.Revision);
    Assert.Equal(
      first.GetPlayer(PlayerSeat.Self).KnownConcealedTiles.Select(tile => tile.Code),
      second.GetPlayer(PlayerSeat.Self).KnownConcealedTiles.Select(tile => tile.Code));
  }

  [Fact]
  public void Apply_is_idempotent_for_the_latest_identical_event()
  {
    var started = Started(1, PlayerSeat.Self);
    var state = reducer.Apply(GameState.Empty, started);

    var retried = reducer.Apply(state, started);

    Assert.Same(state, retried);
  }

  [Fact]
  public void Apply_rejects_idempotency_key_reused_with_different_payload()
  {
    var started = Started(1, PlayerSeat.Self);
    var state = reducer.Apply(GameState.Empty, started);
    var conflicting = new RoundStartedEvent(
      started.EventId,
      started.SessionId,
      started.Sequence,
      started.OccurredAtUtc,
      started.Provenance,
      started.RuleProfileVersion,
      PlayerSeat.Next);

    Assert.Throws<GameStateTransitionException>(() => reducer.Apply(state, conflicting));
  }

  [Fact]
  public void Apply_rejects_out_of_order_and_cross_session_events()
  {
    var state = reducer.Apply(GameState.Empty, Started(1, PlayerSeat.Self));
    var outOfOrder = new TurnChangedEvent(
      EventId(),
      SessionId,
      3,
      OccurredAtUtc,
      GameEventProvenance.System,
      PlayerSeat.Next);
    var crossSession = new TurnChangedEvent(
      EventId(),
      Guid.Parse("22222222-2222-2222-2222-222222222222"),
      2,
      OccurredAtUtc,
      GameEventProvenance.System,
      PlayerSeat.Next);

    Assert.Throws<GameStateTransitionException>(() => reducer.Apply(state, outOfOrder));
    Assert.Throws<GameStateTransitionException>(() => reducer.Apply(state, crossSession));
  }

  [Fact]
  public void Apply_rejects_a_fifth_visible_copy_of_a_tile()
  {
    var state = reducer.Apply(GameState.Empty, Started(1, PlayerSeat.Self));
    state = reducer.Apply(
      state,
      SelfHand(2,
      [
        "5p", "5p", "5p", "5p",
        "1m", "2m", "3m", "4m", "5m", "6m", "7m", "8m", "9m", "1s"
      ]));
    var fifthCopy = new IndicatorRevealedEvent(
      EventId(),
      SessionId,
      3,
      OccurredAtUtc,
      GameEventProvenance.System,
      IndicatorKind.Wildcard,
      TileType.Parse("5p"));

    Assert.Throws<GameStateTransitionException>(() => reducer.Apply(state, fifthCopy));
  }

  [Fact]
  public void Apply_rejects_a_self_discard_missing_from_a_fully_known_hand()
  {
    var state = reducer.Apply(GameState.Empty, Started(1, PlayerSeat.Self));
    state = reducer.Apply(state, SelfHand(2, InitialDealerHand()));

    Assert.Throws<GameStateTransitionException>(() =>
      reducer.Apply(state, Discarded(3, PlayerSeat.Self, "9s")));
  }

  [Fact]
  public void Apply_rejects_discard_without_a_draw_when_structure_would_drop_below_thirteen()
  {
    var state = reducer.Apply(GameState.Empty, Started(1, PlayerSeat.Self));

    var exception = Assert.Throws<GameStateTransitionException>(() =>
      reducer.Apply(state, Discarded(2, PlayerSeat.Next, "9s")));

    Assert.Contains("structural tile count 12", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Apply_rejects_event_time_moving_backwards()
  {
    var state = reducer.Apply(GameState.Empty, Started(1, PlayerSeat.Self));
    var earlier = new TurnChangedEvent(
      EventId(),
      SessionId,
      2,
      OccurredAtUtc.AddSeconds(-1),
      GameEventProvenance.System,
      PlayerSeat.Next);

    Assert.Throws<GameStateTransitionException>(() => reducer.Apply(state, earlier));
  }

  [Fact]
  public void Claimed_meld_moves_the_last_discard_and_updates_concealed_count()
  {
    var state = reducer.Apply(GameState.Empty, Started(1, PlayerSeat.Previous));
    state = reducer.Apply(state, Discarded(2, PlayerSeat.Previous, "3m"));
    var meld = new MeldDeclaredEvent(
      EventId(),
      SessionId,
      3,
      OccurredAtUtc,
      GameEventProvenance.System,
      PlayerSeat.Next,
      MeldType.Chow,
      Codes("2m", "3m", "4m"),
      PlayerSeat.Previous);

    state = reducer.Apply(state, meld);

    Assert.Empty(state.GetPlayer(PlayerSeat.Previous).Discards);
    Assert.Equal(11, state.GetPlayer(PlayerSeat.Next).ConcealedTileCount);
    var declared = Assert.Single(state.GetPlayer(PlayerSeat.Next).Melds);
    Assert.Equal(MeldType.Chow, declared.MeldType);
    Assert.Equal(new[] { "2m", "3m", "4m" }, declared.Tiles.Select(tile => tile.Code));
    Assert.Equal(1, state.CountVisibleCopies(TileType.Parse("3m")));
  }

  [Fact]
  public void Completed_round_rejects_later_events()
  {
    var state = reducer.Apply(GameState.Empty, Started(1, PlayerSeat.Self));
    state = reducer.Apply(
      state,
      new RoundEndedEvent(
        EventId(),
        SessionId,
        2,
        OccurredAtUtc,
        GameEventProvenance.System,
        RoundEndReason.Draw));

    Assert.Throws<GameStateTransitionException>(() =>
      reducer.Apply(state, TurnChanged(3, PlayerSeat.Next)));
  }

  private RoundStartedEvent Started(long sequence, PlayerSeat dealer) => new(
    EventId(),
    SessionId,
    sequence,
    OccurredAtUtc,
    GameEventProvenance.System,
    "wuhan-preview-v1",
    dealer);

  private SelfHandSnapshotConfirmedEvent SelfHand(long sequence, IEnumerable<string> codes) => new(
    EventId(),
    SessionId,
    sequence,
    OccurredAtUtc,
    GameEventProvenance.System,
    codes.Select(TileType.Parse),
    0);

  private TileDrawnEvent Drawn(long sequence, PlayerSeat player, string? code = null) => new(
    EventId(),
    SessionId,
    sequence,
    OccurredAtUtc,
    GameEventProvenance.System,
    player,
    code is null ? null : TileType.Parse(code));

  private TileDiscardedEvent Discarded(long sequence, PlayerSeat player, string code) => new(
    EventId(),
    SessionId,
    sequence,
    OccurredAtUtc,
    GameEventProvenance.System,
    player,
    TileType.Parse(code));

  private TurnChangedEvent TurnChanged(long sequence, PlayerSeat player) => new(
    EventId(),
    SessionId,
    sequence,
    OccurredAtUtc,
    GameEventProvenance.System,
    player);

  private Guid EventId() => Guid.Parse($"00000000-0000-0000-0000-{++eventCounter:000000000000}");

  private static IEnumerable<TileType> Codes(params string[] codes) =>
    codes.Select(TileType.Parse);

  private static string[] InitialDealerHand() =>
    ["1m", "2m", "3m", "4m", "5m", "6m", "7m", "8m", "9m", "1p", "2p", "3p", "4p", "5p"];
}
