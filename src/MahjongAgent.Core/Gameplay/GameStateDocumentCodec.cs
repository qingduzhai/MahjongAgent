using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Gameplay;

public static class GameStateDocumentCodec
{
  public const string SchemaVersion = "game-state/v1";
  private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

  public static string Serialize(GameState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    ValidateState(state);
    var document = ToDocument(state);
    var payload = JsonSerializer.SerializeToElement(document, SerializerOptions);
    var envelope = new StateEnvelope
    {
      SchemaVersion = SchemaVersion,
      Checksum = ComputeChecksum(JsonSerializer.Serialize(document, SerializerOptions)),
      State = payload
    };
    return JsonSerializer.Serialize(envelope, SerializerOptions);
  }

  public static GameState Deserialize(string json)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(json);
    try
    {
      var envelope = JsonSerializer.Deserialize<StateEnvelope>(json, SerializerOptions) ??
        throw new GameStateDocumentException("Game state document was empty.");
      if (!string.Equals(envelope.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
      {
        throw new GameStateDocumentException(
          $"Unsupported game state schema version '{envelope.SchemaVersion}'.");
      }

      var document = envelope.State.Deserialize<StateDocument>(SerializerOptions) ??
        throw new GameStateDocumentException("Game state payload was empty.");
      var actualChecksum = ComputeChecksum(JsonSerializer.Serialize(document, SerializerOptions));
      if (!string.Equals(envelope.Checksum, actualChecksum, StringComparison.Ordinal))
      {
        throw new GameStateDocumentException("Game state checksum verification failed.");
      }

      var players = document.Players.ToImmutableDictionary(
        player => player.Seat,
        player => new PlayerGameState(
          ParseTiles(player.KnownConcealedTiles),
          player.UnknownConcealedTileCount,
          ParseTiles(player.Discards),
          player.Melds.Select(meld => new MeldState(
            meld.MeldType,
            ParseTiles(meld.Tiles),
            meld.ClaimedFrom,
            meld.DeclaredAtSequence)).ToImmutableArray()));
      var state = new GameState
      {
        SessionId = document.SessionId,
        Revision = document.Revision,
        LastEventId = document.LastEventId,
        LastEventFingerprint = document.LastEventFingerprint,
        LastEventKind = document.LastEventKind,
        LastActor = document.LastActor,
        LastEventAtUtc = document.LastEventAtUtc,
        Phase = document.Phase,
        RuleProfileVersion = document.RuleProfileVersion,
        Dealer = document.Dealer,
        CurrentTurn = document.CurrentTurn,
        Players = players,
        Indicators = document.Indicators.Select(indicator => new IndicatorState(
          indicator.IndicatorKind,
          TileType.Parse(indicator.Tile),
          indicator.RevealedAtSequence)).ToImmutableArray(),
        EndReason = document.EndReason,
        Winner = document.Winner
      };
      ValidateState(state);
      return state;
    }
    catch (GameStateDocumentException)
    {
      throw;
    }
    catch (Exception exception)
    {
      throw new GameStateDocumentException("Game state document is invalid.", exception);
    }
  }

  private static StateDocument ToDocument(GameState state) => new()
  {
    SessionId = state.SessionId,
    Revision = state.Revision,
    LastEventId = state.LastEventId,
    LastEventFingerprint = state.LastEventFingerprint,
    LastEventKind = state.LastEventKind,
    LastActor = state.LastActor,
    LastEventAtUtc = state.LastEventAtUtc,
    Phase = state.Phase,
    RuleProfileVersion = state.RuleProfileVersion,
    Dealer = state.Dealer,
    CurrentTurn = state.CurrentTurn,
    Players = state.Players
      .OrderBy(pair => pair.Key)
      .Select(pair => new PlayerDocument
      {
        Seat = pair.Key,
        KnownConcealedTiles = pair.Value.KnownConcealedTiles.Select(tile => tile.Code).ToArray(),
        UnknownConcealedTileCount = pair.Value.UnknownConcealedTileCount,
        Discards = pair.Value.Discards.Select(tile => tile.Code).ToArray(),
        Melds = pair.Value.Melds.Select(meld => new MeldDocument
        {
          MeldType = meld.MeldType,
          Tiles = meld.Tiles.Select(tile => tile.Code).ToArray(),
          ClaimedFrom = meld.ClaimedFrom,
          DeclaredAtSequence = meld.DeclaredAtSequence
        }).ToArray()
      }).ToArray(),
    Indicators = state.Indicators.Select(indicator => new IndicatorDocument
    {
      IndicatorKind = indicator.IndicatorKind,
      Tile = indicator.Tile.Code,
      RevealedAtSequence = indicator.RevealedAtSequence
    }).ToArray(),
    EndReason = state.EndReason,
    Winner = state.Winner
  };

  private static void ValidateState(GameState state)
  {
    if (state.SessionId == Guid.Empty || state.Revision < 1)
    {
      throw new GameStateDocumentException(
        "A persisted game state must identify a started session and positive revision.");
    }

    if (state.LastEventId is null ||
        state.LastEventKind is null ||
        state.LastEventAtUtc is null ||
        !IsSha256(state.LastEventFingerprint))
    {
      throw new GameStateDocumentException(
        "A persisted game state must identify its latest event and fingerprint.");
    }

    if (state.LastEventAtUtc.Value.Offset != TimeSpan.Zero)
    {
      throw new GameStateDocumentException("Game state timestamps must use UTC offset zero.");
    }

    if (state.Phase is GameSessionPhase.NotStarted ||
        string.IsNullOrWhiteSpace(state.RuleProfileVersion) ||
        state.Dealer is null)
    {
      throw new GameStateDocumentException("Game state session metadata is incomplete.");
    }

    if (state.Players.Count != 4 ||
        Enum.GetValues<PlayerSeat>().Any(seat => !state.Players.ContainsKey(seat)))
    {
      throw new GameStateDocumentException("Game state must contain each relative player seat exactly once.");
    }

    foreach (var player in state.Players.Values)
    {
      if (player.UnknownConcealedTileCount < 0 ||
          player.StructuralTileCount is < 13 or > 14)
      {
        throw new GameStateDocumentException("Game state contains an invalid concealed hand size.");
      }

      foreach (var meld in player.Melds)
      {
        var expectedCount = meld.MeldType is MeldType.Chow or MeldType.Pung ? 3 : 4;
        if (meld.Tiles.Length != expectedCount || meld.DeclaredAtSequence is < 1)
        {
          throw new GameStateDocumentException("Game state contains an invalid meld.");
        }
      }
    }

    foreach (var tile in StandardTileSet.Types)
    {
      if (state.CountVisibleCopies(tile) > StandardTileSet.CopiesPerType)
      {
        throw new GameStateDocumentException(
          $"Game state exceeds the physical copy limit for '{tile.Code}'.");
      }
    }

    if (state.Phase is GameSessionPhase.InProgress &&
        (state.CurrentTurn is null || state.EndReason is not null || state.Winner is not null))
    {
      throw new GameStateDocumentException("In-progress game state has invalid completion metadata.");
    }

    if (state.Phase is GameSessionPhase.Completed &&
        (state.CurrentTurn is not null || state.EndReason is null ||
         (state.EndReason is RoundEndReason.Win) != state.Winner.HasValue))
    {
      throw new GameStateDocumentException("Completed game state has invalid outcome metadata.");
    }
  }

  private static ImmutableArray<TileType> ParseTiles(IEnumerable<string> codes) =>
    codes.Select(TileType.Parse).ToImmutableArray();

  private static string ComputeChecksum(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

  private static bool IsSha256(string? value) =>
    value is { Length: 64 } && value.All(character =>
      character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

  private sealed record StateEnvelope
  {
    public required string SchemaVersion { get; init; }

    public required string Checksum { get; init; }

    public required JsonElement State { get; init; }
  }

  private sealed record StateDocument
  {
    public required Guid SessionId { get; init; }

    public required long Revision { get; init; }

    public required Guid? LastEventId { get; init; }

    public required string? LastEventFingerprint { get; init; }

    public required GameEventKind? LastEventKind { get; init; }

    public required PlayerSeat? LastActor { get; init; }

    public required DateTimeOffset? LastEventAtUtc { get; init; }

    public required GameSessionPhase Phase { get; init; }

    public required string? RuleProfileVersion { get; init; }

    public required PlayerSeat? Dealer { get; init; }

    public required PlayerSeat? CurrentTurn { get; init; }

    public required IReadOnlyList<PlayerDocument> Players { get; init; }

    public required IReadOnlyList<IndicatorDocument> Indicators { get; init; }

    public required RoundEndReason? EndReason { get; init; }

    public required PlayerSeat? Winner { get; init; }
  }

  private sealed record PlayerDocument
  {
    public required PlayerSeat Seat { get; init; }

    public required IReadOnlyList<string> KnownConcealedTiles { get; init; }

    public required int UnknownConcealedTileCount { get; init; }

    public required IReadOnlyList<string> Discards { get; init; }

    public required IReadOnlyList<MeldDocument> Melds { get; init; }
  }

  private sealed record MeldDocument
  {
    public required MeldType MeldType { get; init; }

    public required IReadOnlyList<string> Tiles { get; init; }

    public required PlayerSeat? ClaimedFrom { get; init; }

    public required long DeclaredAtSequence { get; init; }
  }

  private sealed record IndicatorDocument
  {
    public required IndicatorKind IndicatorKind { get; init; }

    public required string Tile { get; init; }

    public required long RevealedAtSequence { get; init; }
  }
}

public sealed class GameStateDocumentException : FormatException
{
  public GameStateDocumentException(string message)
    : base(message)
  {
  }

  public GameStateDocumentException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
