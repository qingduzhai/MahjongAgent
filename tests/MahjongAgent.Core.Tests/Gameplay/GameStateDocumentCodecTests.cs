using System.Text.Json.Nodes;
using MahjongAgent.Core.Gameplay;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Tests.Gameplay;

public sealed class GameStateDocumentCodecTests
{
  private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
  private static readonly DateTimeOffset OccurredAtUtc =
    new(2026, 7, 31, 4, 5, 6, TimeSpan.Zero);
  private readonly GameStateReducer reducer = new();
  private int eventCounter;

  [Fact]
  public void Codec_round_trips_in_progress_state_deterministically()
  {
    var state = CreateInProgressState();

    var json = GameStateDocumentCodec.Serialize(state);
    var restored = GameStateDocumentCodec.Deserialize(json);

    Assert.Equal(state.SessionId, restored.SessionId);
    Assert.Equal(state.Revision, restored.Revision);
    Assert.Equal(state.LastEventFingerprint, restored.LastEventFingerprint);
    Assert.Equal(state.Phase, restored.Phase);
    Assert.Equal(state.CurrentTurn, restored.CurrentTurn);
    Assert.Equal(
      state.GetPlayer(PlayerSeat.Self).KnownConcealedTiles.Select(tile => tile.Code),
      restored.GetPlayer(PlayerSeat.Self).KnownConcealedTiles.Select(tile => tile.Code));
    Assert.Equal(
      state.GetPlayer(PlayerSeat.Next).Discards.Select(tile => tile.Code),
      restored.GetPlayer(PlayerSeat.Next).Discards.Select(tile => tile.Code));
    Assert.Equal(json, GameStateDocumentCodec.Serialize(restored));
  }

  [Fact]
  public void Codec_round_trips_completed_state()
  {
    var state = CreateInProgressState();
    state = reducer.Apply(
      state,
      new RoundEndedEvent(
        EventId(),
        SessionId,
        state.Revision + 1,
        OccurredAtUtc.AddSeconds(state.Revision + 1),
        GameEventProvenance.System,
        RoundEndReason.Draw));

    var restored = GameStateDocumentCodec.Deserialize(GameStateDocumentCodec.Serialize(state));

    Assert.Equal(GameSessionPhase.Completed, restored.Phase);
    Assert.Equal(RoundEndReason.Draw, restored.EndReason);
    Assert.Null(restored.CurrentTurn);
  }

  [Fact]
  public void Deserialize_rejects_payload_tampering_with_stale_checksum()
  {
    var document = JsonNode.Parse(
      GameStateDocumentCodec.Serialize(CreateInProgressState()))!.AsObject();
    document["state"]!["current_turn"] = "opposite";

    var exception = Assert.Throws<GameStateDocumentException>(() =>
      GameStateDocumentCodec.Deserialize(document.ToJsonString()));

    Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Deserialize_rejects_unknown_document_fields()
  {
    var document = JsonNode.Parse(
      GameStateDocumentCodec.Serialize(CreateInProgressState()))!.AsObject();
    document["unexpected"] = true;

    Assert.Throws<GameStateDocumentException>(() =>
      GameStateDocumentCodec.Deserialize(document.ToJsonString()));
  }

  private GameState CreateInProgressState()
  {
    GameState state = GameState.Empty;
    state = reducer.Apply(
      state,
      new RoundStartedEvent(
        EventId(),
        SessionId,
        1,
        OccurredAtUtc.AddSeconds(1),
        GameEventProvenance.System,
        "wuhan-v1",
        PlayerSeat.Self));
    state = reducer.Apply(
      state,
      new SelfHandSnapshotConfirmedEvent(
        EventId(),
        SessionId,
        2,
        OccurredAtUtc.AddSeconds(2),
        GameEventProvenance.System,
        InitialDealerHand().Select(TileType.Parse),
        0));
    state = reducer.Apply(
      state,
      new TileDiscardedEvent(
        EventId(),
        SessionId,
        3,
        OccurredAtUtc.AddSeconds(3),
        GameEventProvenance.System,
        PlayerSeat.Self,
        TileType.Parse("5p")));
    state = reducer.Apply(
      state,
      new TurnChangedEvent(
        EventId(),
        SessionId,
        4,
        OccurredAtUtc.AddSeconds(4),
        GameEventProvenance.System,
        PlayerSeat.Next));
    state = reducer.Apply(
      state,
      new TileDrawnEvent(
        EventId(),
        SessionId,
        5,
        OccurredAtUtc.AddSeconds(5),
        GameEventProvenance.System,
        PlayerSeat.Next,
        null));
    state = reducer.Apply(
      state,
      new TileDiscardedEvent(
        EventId(),
        SessionId,
        6,
        OccurredAtUtc.AddSeconds(6),
        GameEventProvenance.System,
        PlayerSeat.Next,
        TileType.Parse("9s")));
    return reducer.Apply(
      state,
      new IndicatorRevealedEvent(
        EventId(),
        SessionId,
        7,
        OccurredAtUtc.AddSeconds(7),
        GameEventProvenance.System,
        IndicatorKind.Wildcard,
        TileType.Parse("6p")));
  }

  private Guid EventId() => Guid.Parse($"00000000-0000-0000-0000-{++eventCounter:000000000000}");

  private static string[] InitialDealerHand() =>
    ["1m", "2m", "3m", "4m", "5m", "6m", "7m", "8m", "9m", "1p", "2p", "3p", "4p", "5p"];
}
