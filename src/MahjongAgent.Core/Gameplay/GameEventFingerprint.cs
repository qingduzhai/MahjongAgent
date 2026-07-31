using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MahjongAgent.Core.Gameplay;

internal static class GameEventFingerprint
{
  public static string Compute(GameEvent gameEvent)
  {
    var builder = new StringBuilder();
    builder
      .Append(gameEvent.Kind.ToProtocolValue()).Append('|')
      .Append(gameEvent.EventId.ToString("N")).Append('|')
      .Append(gameEvent.SessionId.ToString("N")).Append('|')
      .Append(gameEvent.Sequence).Append('|')
      .Append(gameEvent.OccurredAtUtc.Ticks).Append('|')
      .Append(gameEvent.Provenance.SourceKind).Append('|')
      .Append(gameEvent.Provenance.Confidence.ToString("R", CultureInfo.InvariantCulture)).Append('|')
      .AppendJoin(',', gameEvent.Provenance.SourceIds).Append('|');

    switch (gameEvent)
    {
      case RoundStartedEvent started:
        builder.Append(started.RuleProfileVersion).Append('|').Append(started.Dealer);
        break;
      case SelfHandSnapshotConfirmedEvent snapshot:
        builder.AppendJoin(',', snapshot.KnownTiles.Select(tile => tile.Code))
          .Append('|')
          .Append(snapshot.UnknownTileCount);
        break;
      case TileDrawnEvent drawn:
        builder.Append(drawn.Player).Append('|').Append(drawn.Tile?.Code ?? "unknown");
        break;
      case TileDiscardedEvent discarded:
        builder.Append(discarded.Player).Append('|').Append(discarded.Tile.Code);
        break;
      case MeldDeclaredEvent meld:
        builder.Append(meld.Player).Append('|')
          .Append(meld.MeldType).Append('|')
          .AppendJoin(',', meld.Tiles.Select(tile => tile.Code)).Append('|')
          .Append(meld.ClaimedFrom?.ToString() ?? "none");
        break;
      case IndicatorRevealedEvent indicator:
        builder.Append(indicator.IndicatorKind).Append('|').Append(indicator.Tile.Code);
        break;
      case TurnChangedEvent turn:
        builder.Append(turn.CurrentPlayer);
        break;
      case RoundEndedEvent ended:
        builder.Append(ended.Reason).Append('|').Append(ended.Winner?.ToString() ?? "none");
        break;
      default:
        throw new InvalidOperationException(
          $"Unsupported game event type '{gameEvent.GetType().Name}'.");
    }

    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
      .ToLowerInvariant();
  }
}
