using System.Text.Json;
using System.Text.Json.Serialization;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Gameplay;

public static class GameEventDocumentCodec
{
  public const string SchemaVersion = "game-event/v1";
  private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

  public static string Serialize(GameEvent gameEvent)
  {
    ArgumentNullException.ThrowIfNull(gameEvent);
    var document = new EventDocument
    {
      SchemaVersion = SchemaVersion,
      EventType = gameEvent.Kind.ToProtocolValue(),
      EventId = gameEvent.EventId,
      SessionId = gameEvent.SessionId,
      Sequence = gameEvent.Sequence,
      OccurredAtUtc = gameEvent.OccurredAtUtc,
      Provenance = new ProvenanceDocument
      {
        SourceKind = gameEvent.Provenance.SourceKind,
        Confidence = gameEvent.Provenance.Confidence,
        SourceIds = gameEvent.Provenance.SourceIds
      },
      Payload = SerializePayload(gameEvent),
      Fingerprint = gameEvent.Fingerprint
    };
    return JsonSerializer.Serialize(document, SerializerOptions);
  }

  public static GameEvent Deserialize(string json)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(json);
    try
    {
      var document = JsonSerializer.Deserialize<EventDocument>(json, SerializerOptions) ??
        throw new GameEventDocumentException("Game event document was empty.");
      if (!string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
      {
        throw new GameEventDocumentException(
          $"Unsupported game event schema version '{document.SchemaVersion}'.");
      }

      var provenance = new GameEventProvenance(
        document.Provenance.SourceKind,
        document.Provenance.Confidence,
        document.Provenance.SourceIds);
      var gameEvent = DeserializeEvent(document, provenance);
      if (!string.Equals(
            document.EventType,
            gameEvent.Kind.ToProtocolValue(),
            StringComparison.Ordinal))
      {
        throw new GameEventDocumentException(
          "Game event type does not match the decoded payload.");
      }

      if (!string.Equals(document.Fingerprint, gameEvent.Fingerprint, StringComparison.Ordinal))
      {
        throw new GameEventDocumentException("Game event fingerprint verification failed.");
      }

      return gameEvent;
    }
    catch (GameEventDocumentException)
    {
      throw;
    }
    catch (Exception exception)
    {
      throw new GameEventDocumentException(
        "Game event document is invalid.",
        exception);
    }
  }

  private static GameEvent DeserializeEvent(
    EventDocument document,
    GameEventProvenance provenance) => document.EventType switch
    {
      "round_started" => CreateRoundStarted(document, provenance),
      "self_hand_snapshot_confirmed" => CreateSelfHandSnapshot(document, provenance),
      "tile_drawn" => CreateTileDrawn(document, provenance),
      "tile_discarded" => CreateTileDiscarded(document, provenance),
      "meld_declared" => CreateMeldDeclared(document, provenance),
      "indicator_revealed" => CreateIndicatorRevealed(document, provenance),
      "turn_changed" => CreateTurnChanged(document, provenance),
      "round_ended" => CreateRoundEnded(document, provenance),
      _ => throw new GameEventDocumentException(
        $"Unsupported game event type '{document.EventType}'.")
    };

  private static RoundStartedEvent CreateRoundStarted(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<RoundStartedPayload>(document);
    return new RoundStartedEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.RuleProfileVersion,
      payload.Dealer);
  }

  private static SelfHandSnapshotConfirmedEvent CreateSelfHandSnapshot(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<SelfHandSnapshotPayload>(document);
    return new SelfHandSnapshotConfirmedEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.KnownTiles.Select(TileType.Parse),
      payload.UnknownTileCount);
  }

  private static TileDrawnEvent CreateTileDrawn(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<TileDrawnPayload>(document);
    return new TileDrawnEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.Player,
      payload.Tile is null ? null : TileType.Parse(payload.Tile));
  }

  private static TileDiscardedEvent CreateTileDiscarded(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<TileDiscardedPayload>(document);
    return new TileDiscardedEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.Player,
      TileType.Parse(payload.Tile));
  }

  private static MeldDeclaredEvent CreateMeldDeclared(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<MeldDeclaredPayload>(document);
    return new MeldDeclaredEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.Player,
      payload.MeldType,
      payload.Tiles.Select(TileType.Parse),
      payload.ClaimedFrom);
  }

  private static IndicatorRevealedEvent CreateIndicatorRevealed(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<IndicatorRevealedPayload>(document);
    return new IndicatorRevealedEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.IndicatorKind,
      TileType.Parse(payload.Tile));
  }

  private static TurnChangedEvent CreateTurnChanged(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<TurnChangedPayload>(document);
    return new TurnChangedEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.CurrentPlayer);
  }

  private static RoundEndedEvent CreateRoundEnded(
    EventDocument document,
    GameEventProvenance provenance)
  {
    var payload = ReadPayload<RoundEndedPayload>(document);
    return new RoundEndedEvent(
      document.EventId,
      document.SessionId,
      document.Sequence,
      document.OccurredAtUtc,
      provenance,
      payload.Reason,
      payload.Winner);
  }

  private static JsonElement SerializePayload(GameEvent gameEvent) => gameEvent switch
  {
    RoundStartedEvent started => JsonSerializer.SerializeToElement(
      new RoundStartedPayload
      {
        RuleProfileVersion = started.RuleProfileVersion,
        Dealer = started.Dealer
      },
      SerializerOptions),
    SelfHandSnapshotConfirmedEvent snapshot => JsonSerializer.SerializeToElement(
      new SelfHandSnapshotPayload
      {
        KnownTiles = snapshot.KnownTiles.Select(tile => tile.Code).ToArray(),
        UnknownTileCount = snapshot.UnknownTileCount
      },
      SerializerOptions),
    TileDrawnEvent drawn => JsonSerializer.SerializeToElement(
      new TileDrawnPayload
      {
        Player = drawn.Player,
        Tile = drawn.Tile?.Code
      },
      SerializerOptions),
    TileDiscardedEvent discarded => JsonSerializer.SerializeToElement(
      new TileDiscardedPayload
      {
        Player = discarded.Player,
        Tile = discarded.Tile.Code
      },
      SerializerOptions),
    MeldDeclaredEvent meld => JsonSerializer.SerializeToElement(
      new MeldDeclaredPayload
      {
        Player = meld.Player,
        MeldType = meld.MeldType,
        Tiles = meld.Tiles.Select(tile => tile.Code).ToArray(),
        ClaimedFrom = meld.ClaimedFrom
      },
      SerializerOptions),
    IndicatorRevealedEvent indicator => JsonSerializer.SerializeToElement(
      new IndicatorRevealedPayload
      {
        IndicatorKind = indicator.IndicatorKind,
        Tile = indicator.Tile.Code
      },
      SerializerOptions),
    TurnChangedEvent turn => JsonSerializer.SerializeToElement(
      new TurnChangedPayload { CurrentPlayer = turn.CurrentPlayer },
      SerializerOptions),
    RoundEndedEvent ended => JsonSerializer.SerializeToElement(
      new RoundEndedPayload
      {
        Reason = ended.Reason,
        Winner = ended.Winner
      },
      SerializerOptions),
    _ => throw new ArgumentException(
      $"Unsupported game event type '{gameEvent.GetType().Name}'.",
      nameof(gameEvent))
  };

  private static TPayload ReadPayload<TPayload>(EventDocument document)
    where TPayload : class =>
    document.Payload.Deserialize<TPayload>(SerializerOptions) ??
    throw new GameEventDocumentException(
      $"Payload for event type '{document.EventType}' was empty.");

  private static JsonSerializerOptions CreateSerializerOptions()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
      UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false));
    return options;
  }

  private sealed record EventDocument
  {
    public required string SchemaVersion { get; init; }

    public required string EventType { get; init; }

    public required Guid EventId { get; init; }

    public required Guid SessionId { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required ProvenanceDocument Provenance { get; init; }

    public required JsonElement Payload { get; init; }

    public required string Fingerprint { get; init; }
  }

  private sealed record ProvenanceDocument
  {
    public required GameEventSourceKind SourceKind { get; init; }

    public required double Confidence { get; init; }

    public required IReadOnlyList<string> SourceIds { get; init; }
  }

  private sealed record RoundStartedPayload
  {
    public required string RuleProfileVersion { get; init; }

    public required PlayerSeat Dealer { get; init; }
  }

  private sealed record SelfHandSnapshotPayload
  {
    public required IReadOnlyList<string> KnownTiles { get; init; }

    public required int UnknownTileCount { get; init; }
  }

  private sealed record TileDrawnPayload
  {
    public required PlayerSeat Player { get; init; }

    public string? Tile { get; init; }
  }

  private sealed record TileDiscardedPayload
  {
    public required PlayerSeat Player { get; init; }

    public required string Tile { get; init; }
  }

  private sealed record MeldDeclaredPayload
  {
    public required PlayerSeat Player { get; init; }

    public required MeldType MeldType { get; init; }

    public required IReadOnlyList<string> Tiles { get; init; }

    public PlayerSeat? ClaimedFrom { get; init; }
  }

  private sealed record IndicatorRevealedPayload
  {
    public required IndicatorKind IndicatorKind { get; init; }

    public required string Tile { get; init; }
  }

  private sealed record TurnChangedPayload
  {
    public required PlayerSeat CurrentPlayer { get; init; }
  }

  private sealed record RoundEndedPayload
  {
    public required RoundEndReason Reason { get; init; }

    public PlayerSeat? Winner { get; init; }
  }
}

public sealed class GameEventDocumentException : FormatException
{
  public GameEventDocumentException(string message)
    : base(message)
  {
  }

  public GameEventDocumentException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
