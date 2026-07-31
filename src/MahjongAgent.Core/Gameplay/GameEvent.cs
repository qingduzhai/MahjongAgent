using System.Collections.Immutable;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Gameplay;

public enum GameEventSourceKind
{
  Perception,
  UserCorrection,
  Replay,
  System
}

public enum GameEventKind
{
  RoundStarted,
  SelfHandSnapshotConfirmed,
  TileDrawn,
  TileDiscarded,
  MeldDeclared,
  IndicatorRevealed,
  TurnChanged,
  RoundEnded
}

public static class GameEventKindExtensions
{
  public static string ToProtocolValue(this GameEventKind kind) => kind switch
  {
    GameEventKind.RoundStarted => "round_started",
    GameEventKind.SelfHandSnapshotConfirmed => "self_hand_snapshot_confirmed",
    GameEventKind.TileDrawn => "tile_drawn",
    GameEventKind.TileDiscarded => "tile_discarded",
    GameEventKind.MeldDeclared => "meld_declared",
    GameEventKind.IndicatorRevealed => "indicator_revealed",
    GameEventKind.TurnChanged => "turn_changed",
    GameEventKind.RoundEnded => "round_ended",
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported game event kind.")
  };
}

public sealed record GameEventProvenance
{
  public GameEventProvenance(
    GameEventSourceKind sourceKind,
    double confidence,
    IEnumerable<string>? sourceIds = null)
  {
    GameplayGuard.DefinedEnum(sourceKind, nameof(sourceKind));
    if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
    {
      throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
    }

    SourceKind = sourceKind;
    Confidence = confidence;
    SourceIds = (sourceIds ?? [])
      .Select(sourceId => GameplayGuard.Identifier(sourceId, nameof(sourceIds), 128))
      .Distinct(StringComparer.Ordinal)
      .ToImmutableArray();
  }

  public GameEventSourceKind SourceKind { get; }

  public double Confidence { get; }

  public ImmutableArray<string> SourceIds { get; }

  public static GameEventProvenance System { get; } =
    new(GameEventSourceKind.System, 1);
}

public abstract record GameEvent
{
  protected GameEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance)
  {
    if (eventId == Guid.Empty)
    {
      throw new ArgumentException("Event ID cannot be empty.", nameof(eventId));
    }

    if (sessionId == Guid.Empty)
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    if (sequence < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(sequence), "Event sequence must start at one.");
    }

    ArgumentNullException.ThrowIfNull(provenance);
    EventId = eventId;
    SessionId = sessionId;
    Sequence = sequence;
    OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    Provenance = provenance;
  }

  public Guid EventId { get; }

  public Guid SessionId { get; }

  public long Sequence { get; }

  public DateTimeOffset OccurredAtUtc { get; }

  public GameEventProvenance Provenance { get; }

  public GameEventKind Kind => this switch
  {
    RoundStartedEvent => GameEventKind.RoundStarted,
    SelfHandSnapshotConfirmedEvent => GameEventKind.SelfHandSnapshotConfirmed,
    TileDrawnEvent => GameEventKind.TileDrawn,
    TileDiscardedEvent => GameEventKind.TileDiscarded,
    MeldDeclaredEvent => GameEventKind.MeldDeclared,
    IndicatorRevealedEvent => GameEventKind.IndicatorRevealed,
    TurnChangedEvent => GameEventKind.TurnChanged,
    RoundEndedEvent => GameEventKind.RoundEnded,
    _ => throw new InvalidOperationException(
      $"Unsupported game event type '{GetType().Name}'.")
  };

  public string Fingerprint => GameEventFingerprint.Compute(this);
}

public sealed record RoundStartedEvent : GameEvent
{
  public RoundStartedEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    string ruleProfileVersion,
    PlayerSeat dealer)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    GameplayGuard.DefinedEnum(dealer, nameof(dealer));
    RuleProfileVersion = GameplayGuard.Identifier(
      ruleProfileVersion,
      nameof(ruleProfileVersion),
      128);
    Dealer = dealer;
  }

  public string RuleProfileVersion { get; }

  public PlayerSeat Dealer { get; }
}

public sealed record SelfHandSnapshotConfirmedEvent : GameEvent
{
  public SelfHandSnapshotConfirmedEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    IEnumerable<TileType> knownTiles,
    int unknownTileCount)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    if (unknownTileCount is < 0 or > 14)
    {
      throw new ArgumentOutOfRangeException(nameof(unknownTileCount));
    }

    KnownTiles = GameplayGuard.Tiles(knownTiles, nameof(knownTiles), 14);
    if (KnownTiles.Length + unknownTileCount is < 1 or > 14)
    {
      throw new ArgumentException("A concealed hand snapshot must contain between one and fourteen tiles.");
    }

    UnknownTileCount = unknownTileCount;
  }

  public ImmutableArray<TileType> KnownTiles { get; }

  public int UnknownTileCount { get; }
}

public sealed record TileDrawnEvent : GameEvent
{
  public TileDrawnEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    PlayerSeat player,
    TileType? tile)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    GameplayGuard.DefinedEnum(player, nameof(player));
    Player = player;
    Tile = tile;
  }

  public PlayerSeat Player { get; }

  public TileType? Tile { get; }
}

public sealed record TileDiscardedEvent : GameEvent
{
  public TileDiscardedEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    PlayerSeat player,
    TileType tile)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    ArgumentNullException.ThrowIfNull(tile);
    GameplayGuard.DefinedEnum(player, nameof(player));
    Player = player;
    Tile = tile;
  }

  public PlayerSeat Player { get; }

  public TileType Tile { get; }
}

public enum MeldType
{
  Chow,
  Pung,
  ExposedKong,
  ConcealedKong,
  AddedKong
}

public sealed record MeldDeclaredEvent : GameEvent
{
  public MeldDeclaredEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    PlayerSeat player,
    MeldType meldType,
    IEnumerable<TileType> tiles,
    PlayerSeat? claimedFrom = null)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    GameplayGuard.DefinedEnum(player, nameof(player));
    GameplayGuard.DefinedEnum(meldType, nameof(meldType));
    if (claimedFrom.HasValue)
    {
      GameplayGuard.DefinedEnum(claimedFrom.Value, nameof(claimedFrom));
    }

    Player = player;
    MeldType = meldType;
    Tiles = GameplayGuard.Tiles(tiles, nameof(tiles), 4);
    ClaimedFrom = claimedFrom;
  }

  public PlayerSeat Player { get; }

  public MeldType MeldType { get; }

  public ImmutableArray<TileType> Tiles { get; }

  public PlayerSeat? ClaimedFrom { get; }
}

public enum IndicatorKind
{
  Wildcard,
  Skin,
  Other
}

public sealed record IndicatorRevealedEvent : GameEvent
{
  public IndicatorRevealedEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    IndicatorKind indicatorKind,
    TileType tile)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    ArgumentNullException.ThrowIfNull(tile);
    GameplayGuard.DefinedEnum(indicatorKind, nameof(indicatorKind));
    IndicatorKind = indicatorKind;
    Tile = tile;
  }

  public IndicatorKind IndicatorKind { get; }

  public TileType Tile { get; }
}

public sealed record TurnChangedEvent : GameEvent
{
  public TurnChangedEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    PlayerSeat currentPlayer)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    GameplayGuard.DefinedEnum(currentPlayer, nameof(currentPlayer));
    CurrentPlayer = currentPlayer;
  }

  public PlayerSeat CurrentPlayer { get; }
}

public enum RoundEndReason
{
  Win,
  Draw,
  Aborted,
  Unknown
}

public sealed record RoundEndedEvent : GameEvent
{
  public RoundEndedEvent(
    Guid eventId,
    Guid sessionId,
    long sequence,
    DateTimeOffset occurredAtUtc,
    GameEventProvenance provenance,
    RoundEndReason reason,
    PlayerSeat? winner = null)
    : base(eventId, sessionId, sequence, occurredAtUtc, provenance)
  {
    GameplayGuard.DefinedEnum(reason, nameof(reason));
    if (winner.HasValue)
    {
      GameplayGuard.DefinedEnum(winner.Value, nameof(winner));
    }

    if (reason is RoundEndReason.Win && winner is null)
    {
      throw new ArgumentException("A winning round must identify the winner.", nameof(winner));
    }

    if (reason is not RoundEndReason.Win && winner is not null)
    {
      throw new ArgumentException("Only a winning round can identify a winner.", nameof(winner));
    }

    Reason = reason;
    Winner = winner;
  }

  public RoundEndReason Reason { get; }

  public PlayerSeat? Winner { get; }
}

internal static class GameplayGuard
{
  public static void DefinedEnum<TEnum>(TEnum value, string parameterName)
    where TEnum : struct, Enum
  {
    if (!Enum.IsDefined(value))
    {
      throw new ArgumentOutOfRangeException(parameterName, value, "Undefined enum value.");
    }
  }

  public static string Identifier(string value, string parameterName, int maxLength)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    if (value.Length > maxLength ||
        !char.IsAsciiLetterOrDigit(value[0]) ||
        value.Any(character =>
          !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
    {
      throw new ArgumentException(
        $"'{parameterName}' must be an ASCII protocol identifier of at most {maxLength} characters.",
        parameterName);
    }

    return value;
  }

  public static ImmutableArray<TileType> Tiles(
    IEnumerable<TileType> tiles,
    string parameterName,
    int maxCount)
  {
    ArgumentNullException.ThrowIfNull(tiles, parameterName);
    var result = tiles.ToImmutableArray();
    if (result.Length > maxCount || result.Any(tile => tile is null))
    {
      throw new ArgumentException(
        $"'{parameterName}' contains null or more than {maxCount} tiles.",
        parameterName);
    }

    return result;
  }
}
