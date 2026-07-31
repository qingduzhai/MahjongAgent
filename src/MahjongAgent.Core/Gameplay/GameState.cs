using System.Collections.Immutable;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Gameplay;

public enum GameSessionPhase
{
  NotStarted,
  InProgress,
  Completed
}

public sealed record MeldState(
  MeldType MeldType,
  ImmutableArray<TileType> Tiles,
  PlayerSeat? ClaimedFrom,
  long DeclaredAtSequence);

public sealed record IndicatorState(
  IndicatorKind IndicatorKind,
  TileType Tile,
  long RevealedAtSequence);

public sealed record PlayerGameState
{
  internal PlayerGameState(
    ImmutableArray<TileType> knownConcealedTiles,
    int unknownConcealedTileCount,
    ImmutableArray<TileType> discards,
    ImmutableArray<MeldState> melds)
  {
    KnownConcealedTiles = knownConcealedTiles;
    UnknownConcealedTileCount = unknownConcealedTileCount;
    Discards = discards;
    Melds = melds;
  }

  public ImmutableArray<TileType> KnownConcealedTiles { get; internal init; }

  public int UnknownConcealedTileCount { get; internal init; }

  public ImmutableArray<TileType> Discards { get; internal init; }

  public ImmutableArray<MeldState> Melds { get; internal init; }

  public int ConcealedTileCount => KnownConcealedTiles.Length + UnknownConcealedTileCount;

  public int StructuralTileCount => ConcealedTileCount + Melds.Length * 3;

  public bool HasOpenedHand => Melds.Any(meld => meld.MeldType is not MeldType.ConcealedKong);
}

public sealed record GameState
{
  private GameState()
  {
  }

  public Guid SessionId { get; internal init; }

  public long Revision { get; internal init; }

  public Guid? LastEventId { get; internal init; }

  public string? LastEventFingerprint { get; internal init; }

  public GameEventKind? LastEventKind { get; internal init; }

  public PlayerSeat? LastActor { get; internal init; }

  public DateTimeOffset? LastEventAtUtc { get; internal init; }

  public GameSessionPhase Phase { get; internal init; }

  public string? RuleProfileVersion { get; internal init; }

  public PlayerSeat? Dealer { get; internal init; }

  public PlayerSeat? CurrentTurn { get; internal init; }

  public ImmutableDictionary<PlayerSeat, PlayerGameState> Players { get; internal init; } =
    ImmutableDictionary<PlayerSeat, PlayerGameState>.Empty;

  public ImmutableArray<IndicatorState> Indicators { get; internal init; } = [];

  public RoundEndReason? EndReason { get; internal init; }

  public PlayerSeat? Winner { get; internal init; }

  public static GameState Empty { get; } = new()
  {
    Phase = GameSessionPhase.NotStarted
  };

  public PlayerGameState GetPlayer(PlayerSeat seat) =>
    Players.TryGetValue(seat, out var player)
      ? player
      : throw new InvalidOperationException("The round has not initialized player state.");

  public int CountVisibleCopies(TileType tile)
  {
    ArgumentNullException.ThrowIfNull(tile);
    var count = Indicators.Count(indicator => indicator.Tile == tile);
    foreach (var player in Players.Values)
    {
      count += player.KnownConcealedTiles.Count(candidate => candidate == tile);
      count += player.Discards.Count(candidate => candidate == tile);
      count += player.Melds.Sum(meld => meld.Tiles.Count(candidate => candidate == tile));
    }

    return count;
  }
}
