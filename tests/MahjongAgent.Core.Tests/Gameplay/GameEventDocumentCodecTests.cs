using System.Text.Json.Nodes;
using MahjongAgent.Core.Gameplay;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Tests.Gameplay;

public sealed class GameEventDocumentCodecTests
{
  private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
  private static readonly DateTimeOffset OccurredAtUtc =
    new(2026, 7, 31, 3, 4, 5, TimeSpan.Zero);
  private static readonly GameEventProvenance Provenance = new(
    GameEventSourceKind.Perception,
    0.97,
    ["obs-1", "frame-1"]);

  public static TheoryData<GameEvent> Events => new()
  {
    new RoundStartedEvent(
      EventId(1), SessionId, 1, OccurredAtUtc, Provenance, "wuhan-v1", PlayerSeat.Self),
    new SelfHandSnapshotConfirmedEvent(
      EventId(2), SessionId, 2, OccurredAtUtc, Provenance,
      Codes("1m", "2m", "3m", "4m", "5m", "6m", "7m", "8m", "9m", "1p", "2p", "3p", "4p", "5p"), 0),
    new TileDrawnEvent(
      EventId(3), SessionId, 3, OccurredAtUtc, Provenance, PlayerSeat.Next, null),
    new TileDrawnEvent(
      EventId(4), SessionId, 4, OccurredAtUtc, Provenance, PlayerSeat.Self, TileType.Parse("6p")),
    new TileDiscardedEvent(
      EventId(5), SessionId, 5, OccurredAtUtc, Provenance, PlayerSeat.Self, TileType.Parse("5p")),
    new MeldDeclaredEvent(
      EventId(6), SessionId, 6, OccurredAtUtc, Provenance,
      PlayerSeat.Next, MeldType.Pung, Codes("3m", "3m", "3m"), PlayerSeat.Previous),
    new IndicatorRevealedEvent(
      EventId(7), SessionId, 7, OccurredAtUtc, Provenance, IndicatorKind.Wildcard, TileType.Parse("5p")),
    new TurnChangedEvent(
      EventId(8), SessionId, 8, OccurredAtUtc, Provenance, PlayerSeat.Opposite),
    new RoundEndedEvent(
      EventId(9), SessionId, 9, OccurredAtUtc, Provenance, RoundEndReason.Win, PlayerSeat.Self)
  };

  [Theory]
  [MemberData(nameof(Events))]
  public void Codec_round_trips_every_event_type(GameEvent gameEvent)
  {
    var json = GameEventDocumentCodec.Serialize(gameEvent);

    var restored = GameEventDocumentCodec.Deserialize(json);

    Assert.IsType(gameEvent.GetType(), restored);
    Assert.Equal(gameEvent.EventId, restored.EventId);
    Assert.Equal(gameEvent.SessionId, restored.SessionId);
    Assert.Equal(gameEvent.Sequence, restored.Sequence);
    Assert.Equal(gameEvent.OccurredAtUtc, restored.OccurredAtUtc);
    Assert.Equal(gameEvent.Kind, restored.Kind);
    Assert.Equal(gameEvent.Fingerprint, restored.Fingerprint);
    Assert.Equal(json, GameEventDocumentCodec.Serialize(restored));
  }

  [Fact]
  public void Deserialize_rejects_payload_tampering_with_stale_fingerprint()
  {
    var original = new TileDiscardedEvent(
      EventId(10),
      SessionId,
      10,
      OccurredAtUtc,
      Provenance,
      PlayerSeat.Self,
      TileType.Parse("5p"));
    var document = JsonNode.Parse(GameEventDocumentCodec.Serialize(original))!.AsObject();
    document["payload"]!["tile"] = "6p";

    var exception = Assert.Throws<GameEventDocumentException>(() =>
      GameEventDocumentCodec.Deserialize(document.ToJsonString()));

    Assert.Contains("fingerprint", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Deserialize_rejects_unknown_document_fields()
  {
    var original = new RoundStartedEvent(
      EventId(11),
      SessionId,
      1,
      OccurredAtUtc,
      Provenance,
      "wuhan-v1",
      PlayerSeat.Self);
    var document = JsonNode.Parse(GameEventDocumentCodec.Serialize(original))!.AsObject();
    document["unexpected"] = true;

    Assert.Throws<GameEventDocumentException>(() =>
      GameEventDocumentCodec.Deserialize(document.ToJsonString()));
  }

  private static Guid EventId(int value) =>
    Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

  private static IEnumerable<TileType> Codes(params string[] codes) =>
    codes.Select(TileType.Parse);
}
