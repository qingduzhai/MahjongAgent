using System.Collections.Immutable;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Gameplay;

public sealed class GameStateReducer
{
  public GameState Replay(IEnumerable<GameEvent> events)
  {
    ArgumentNullException.ThrowIfNull(events);
    return events.Aggregate(GameState.Empty, Apply);
  }

  public GameState Apply(GameState state, GameEvent gameEvent)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(gameEvent);

    if (state.LastEventId == gameEvent.EventId && state.Revision == gameEvent.Sequence)
    {
      if (string.Equals(
            state.LastEventFingerprint,
            gameEvent.Fingerprint,
            StringComparison.Ordinal))
      {
        return state;
      }

      throw new GameStateTransitionException(
        "The latest event ID and sequence were reused with a different payload.");
    }

    ValidateEnvelope(state, gameEvent);
    var next = gameEvent switch
    {
      RoundStartedEvent started => ApplyRoundStarted(state, started),
      SelfHandSnapshotConfirmedEvent snapshot => ApplySelfHandSnapshot(state, snapshot),
      TileDrawnEvent drawn => ApplyDraw(state, drawn),
      TileDiscardedEvent discarded => ApplyDiscard(state, discarded),
      MeldDeclaredEvent meld => ApplyMeld(state, meld),
      IndicatorRevealedEvent indicator => ApplyIndicator(state, indicator),
      TurnChangedEvent turn => state with { CurrentTurn = turn.CurrentPlayer },
      RoundEndedEvent ended => state with
      {
        Phase = GameSessionPhase.Completed,
        CurrentTurn = null,
        EndReason = ended.Reason,
        Winner = ended.Winner
      },
      _ => throw new GameStateTransitionException(
        $"Unsupported game event type '{gameEvent.GetType().Name}'.")
    };

    next = next with
    {
      Revision = gameEvent.Sequence,
      LastEventId = gameEvent.EventId,
      LastEventFingerprint = gameEvent.Fingerprint,
      LastEventKind = gameEvent.Kind,
      LastActor = GetActor(gameEvent),
      LastEventAtUtc = gameEvent.OccurredAtUtc
    };
    ValidatePhysicalTileLimit(next);
    ValidateHandStructure(next);
    return next;
  }

  private static void ValidateEnvelope(GameState state, GameEvent gameEvent)
  {
    if (state.Phase is GameSessionPhase.NotStarted)
    {
      if (gameEvent is not RoundStartedEvent || gameEvent.Sequence != 1)
      {
        throw new GameStateTransitionException(
          "The first event must be RoundStarted with sequence one.");
      }

      return;
    }

    if (state.SessionId != gameEvent.SessionId)
    {
      throw new GameStateTransitionException("The event belongs to a different game session.");
    }

    if (gameEvent.Sequence != state.Revision + 1)
    {
      throw new GameStateTransitionException(
        $"Expected event sequence {state.Revision + 1}, received {gameEvent.Sequence}.");
    }

    if (state.LastEventAtUtc is { } lastEventAtUtc && gameEvent.OccurredAtUtc < lastEventAtUtc)
    {
      throw new GameStateTransitionException("Event time cannot move backwards.");
    }

    if (state.Phase is GameSessionPhase.Completed)
    {
      throw new GameStateTransitionException("A completed round cannot accept more events.");
    }

    if (gameEvent is RoundStartedEvent)
    {
      throw new GameStateTransitionException("A game session can contain only one round start event.");
    }
  }

  private static GameState ApplyRoundStarted(GameState state, RoundStartedEvent gameEvent)
  {
    var players = Enum
      .GetValues<PlayerSeat>()
      .ToImmutableDictionary(
        seat => seat,
        seat => new PlayerGameState(
          [],
          seat == gameEvent.Dealer ? 14 : 13,
          [],
          []));

    return state with
    {
      SessionId = gameEvent.SessionId,
      Phase = GameSessionPhase.InProgress,
      RuleProfileVersion = gameEvent.RuleProfileVersion,
      Dealer = gameEvent.Dealer,
      CurrentTurn = gameEvent.Dealer,
      Players = players,
      Indicators = [],
      EndReason = null,
      Winner = null
    };
  }

  private static GameState ApplySelfHandSnapshot(
    GameState state,
    SelfHandSnapshotConfirmedEvent gameEvent)
  {
    var current = state.GetPlayer(PlayerSeat.Self);
    if (gameEvent.KnownTiles.Length + gameEvent.UnknownTileCount != current.ConcealedTileCount)
    {
      throw new GameStateTransitionException(
        "The self hand snapshot does not match the current concealed tile count.");
    }

    return SetPlayer(
      state,
      PlayerSeat.Self,
      current with
      {
        KnownConcealedTiles = gameEvent.KnownTiles,
        UnknownConcealedTileCount = gameEvent.UnknownTileCount
      });
  }

  private static GameState ApplyDraw(GameState state, TileDrawnEvent gameEvent)
  {
    var player = state.GetPlayer(gameEvent.Player);
    player = gameEvent.Tile is null
      ? player with { UnknownConcealedTileCount = player.UnknownConcealedTileCount + 1 }
      : player with { KnownConcealedTiles = player.KnownConcealedTiles.Add(gameEvent.Tile) };
    return SetPlayer(state, gameEvent.Player, player);
  }

  private static GameState ApplyDiscard(GameState state, TileDiscardedEvent gameEvent)
  {
    var player = state.GetPlayer(gameEvent.Player);
    player = RemoveConcealedTiles(player, [gameEvent.Tile]);
    player = player with { Discards = player.Discards.Add(gameEvent.Tile) };
    return SetPlayer(state, gameEvent.Player, player);
  }

  private static GameState ApplyMeld(GameState state, MeldDeclaredEvent gameEvent)
  {
    ValidateMeldShape(gameEvent);
    if (gameEvent.MeldType is MeldType.AddedKong)
    {
      return ApplyAddedKong(state, gameEvent);
    }

    var requiresClaim = gameEvent.MeldType is
      MeldType.Chow or MeldType.Pung or MeldType.ExposedKong;
    if (requiresClaim != gameEvent.ClaimedFrom.HasValue)
    {
      throw new GameStateTransitionException(
        "The meld claimed-from seat does not match the meld type.");
    }

    var concealedContributions = gameEvent.Tiles;
    if (requiresClaim)
    {
      var sourceSeat = gameEvent.ClaimedFrom!.Value;
      if (sourceSeat == gameEvent.Player)
      {
        throw new GameStateTransitionException("A player cannot claim their own discard.");
      }

      var source = state.GetPlayer(sourceSeat);
      if (source.Discards.Length is 0)
      {
        throw new GameStateTransitionException("The claimed player has no discard to claim.");
      }

      var claimedTile = source.Discards[^1];
      var claimedIndex = concealedContributions.IndexOf(claimedTile);
      if (claimedIndex < 0)
      {
        throw new GameStateTransitionException(
          "The claimed discard is not present in the declared meld.");
      }

      concealedContributions = concealedContributions.RemoveAt(claimedIndex);
      state = SetPlayer(
        state,
        sourceSeat,
        source with { Discards = source.Discards.RemoveAt(source.Discards.Length - 1) });
    }

    var player = RemoveConcealedTiles(state.GetPlayer(gameEvent.Player), concealedContributions);
    player = player with
    {
      Melds = player.Melds.Add(new MeldState(
        gameEvent.MeldType,
        gameEvent.Tiles,
        gameEvent.ClaimedFrom,
        gameEvent.Sequence))
    };
    return SetPlayer(state, gameEvent.Player, player);
  }

  private static GameState ApplyAddedKong(GameState state, MeldDeclaredEvent gameEvent)
  {
    if (gameEvent.ClaimedFrom is not null)
    {
      throw new GameStateTransitionException("An added kong cannot claim a new discard.");
    }

    var player = state.GetPlayer(gameEvent.Player);
    var tile = gameEvent.Tiles[0];
    var meldIndex = -1;
    for (var index = 0; index < player.Melds.Length; index++)
    {
      var meld = player.Melds[index];
      if (meld.MeldType is MeldType.Pung && meld.Tiles.All(candidate => candidate == tile))
      {
        meldIndex = index;
        break;
      }
    }
    if (meldIndex < 0)
    {
      throw new GameStateTransitionException("An added kong requires an existing matching pung.");
    }

    player = RemoveConcealedTiles(player, [tile]);
    player = player with
    {
      Melds = player.Melds.SetItem(
        meldIndex,
        new MeldState(
          MeldType.AddedKong,
          gameEvent.Tiles,
          player.Melds[meldIndex].ClaimedFrom,
          gameEvent.Sequence))
    };
    return SetPlayer(state, gameEvent.Player, player);
  }

  private static GameState ApplyIndicator(GameState state, IndicatorRevealedEvent gameEvent) =>
    state with
    {
      Indicators = state.Indicators.Add(new IndicatorState(
        gameEvent.IndicatorKind,
        gameEvent.Tile,
        gameEvent.Sequence))
    };

  private static void ValidateMeldShape(MeldDeclaredEvent gameEvent)
  {
    var expectedCount = gameEvent.MeldType is MeldType.Chow or MeldType.Pung ? 3 : 4;
    if (gameEvent.Tiles.Length != expectedCount)
    {
      throw new GameStateTransitionException(
        $"Meld type '{gameEvent.MeldType}' requires {expectedCount} tiles.");
    }

    if (gameEvent.MeldType is MeldType.Chow)
    {
      var ordered = gameEvent.Tiles.OrderBy(tile => tile.Rank).ToArray();
      if (ordered.Any(tile => !tile.IsSuited) ||
          ordered.Select(tile => tile.Suit).Distinct().Count() != 1 ||
          ordered[0].Rank + 1 != ordered[1].Rank ||
          ordered[1].Rank + 1 != ordered[2].Rank)
      {
        throw new GameStateTransitionException("A chow must be three consecutive tiles in one suit.");
      }

      return;
    }

    if (gameEvent.Tiles.Any(tile => tile != gameEvent.Tiles[0]))
    {
      throw new GameStateTransitionException(
        $"Meld type '{gameEvent.MeldType}' requires identical tiles.");
    }
  }

  private static PlayerGameState RemoveConcealedTiles(
    PlayerGameState player,
    IEnumerable<TileType> tiles)
  {
    var known = player.KnownConcealedTiles;
    var unknown = player.UnknownConcealedTileCount;
    foreach (var tile in tiles)
    {
      var index = known.IndexOf(tile);
      if (index >= 0)
      {
        known = known.RemoveAt(index);
      }
      else if (unknown > 0)
      {
        unknown--;
      }
      else
      {
        throw new GameStateTransitionException(
          $"The concealed hand does not contain tile '{tile.Code}'.");
      }
    }

    return player with
    {
      KnownConcealedTiles = known,
      UnknownConcealedTileCount = unknown
    };
  }

  private static GameState SetPlayer(
    GameState state,
    PlayerSeat seat,
    PlayerGameState player)
  {
    if (player.UnknownConcealedTileCount < 0)
    {
      throw new GameStateTransitionException("Unknown concealed tile count cannot be negative.");
    }

    return state with { Players = state.Players.SetItem(seat, player) };
  }

  private static void ValidatePhysicalTileLimit(GameState state)
  {
    foreach (var tile in StandardTileSet.Types)
    {
      var visibleCopies = state.CountVisibleCopies(tile);
      if (visibleCopies > StandardTileSet.CopiesPerType)
      {
        throw new GameStateTransitionException(
          $"State contains {visibleCopies} visible copies of '{tile.Code}', exceeding the physical limit.");
      }
    }
  }

  private static void ValidateHandStructure(GameState state)
  {
    foreach (var (seat, player) in state.Players)
    {
      if (player.StructuralTileCount is < 13 or > 14)
      {
        throw new GameStateTransitionException(
          $"Player '{seat}' has invalid structural tile count {player.StructuralTileCount}; expected 13 or 14.");
      }
    }
  }

  private static PlayerSeat? GetActor(GameEvent gameEvent) => gameEvent switch
  {
    TileDrawnEvent drawn => drawn.Player,
    TileDiscardedEvent discarded => discarded.Player,
    MeldDeclaredEvent meld => meld.Player,
    TurnChangedEvent turn => turn.CurrentPlayer,
    RoundEndedEvent ended => ended.Winner,
    _ => null
  };
}
