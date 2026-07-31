using MahjongAgent.Core.Gameplay;
using MahjongAgent.Core.Tiles;
using Microsoft.Data.Sqlite;

namespace MahjongAgent.Storage.Tests;

public sealed class SqliteGameEventJournalTests
{
  private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
  private static readonly DateTimeOffset OccurredAtUtc =
    new(2026, 7, 31, 5, 6, 7, TimeSpan.Zero);
  private int eventCounter;

  [Fact]
  public async Task Append_and_recover_round_trip_events_with_periodic_snapshots()
  {
    await using var database = await TestDatabase.CreateAsync(snapshotInterval: 2);
    var events = CompleteInProgressPrefix();

    GameEventAppendResult? last = null;
    foreach (var gameEvent in events)
    {
      last = await database.Journal.AppendAsync(gameEvent);
      Assert.Equal(GameEventAppendStatus.Appended, last.Status);
    }

    var restarted = new SqliteGameEventJournal(
      database.ConnectionFactory,
      timeProvider: new FixedTimeProvider(OccurredAtUtc.AddHours(1)),
      snapshotInterval: 2);
    var restored = await restarted.RecoverAsync(SessionId);

    Assert.NotNull(last);
    Assert.NotNull(restored);
    Assert.Equal(last.GameState.Revision, restored.Revision);
    Assert.Equal(last.GameState.LastEventFingerprint, restored.LastEventFingerprint);
    Assert.Equal("5p", Assert.Single(restored.GetPlayer(PlayerSeat.Self).Discards).Code);
    Assert.Equal("9s", Assert.Single(restored.GetPlayer(PlayerSeat.Next).Discards).Code);
    Assert.Equal("6p", Assert.Single(restored.Indicators).Tile.Code);
    Assert.Equal([SessionId], await restarted.ListActiveSessionIdsAsync());
    Assert.Equal(3, await ScalarIntAsync(
      database.AnchorConnection,
      "SELECT COUNT(*) FROM game_state_snapshots;"));
  }

  [Fact]
  public async Task Append_is_idempotent_for_the_same_event_document()
  {
    await using var database = await TestDatabase.CreateAsync();
    var started = Started(1, PlayerSeat.Self);

    var first = await database.Journal.AppendAsync(started);
    var second = await database.Journal.AppendAsync(started);

    Assert.Equal(GameEventAppendStatus.Appended, first.Status);
    Assert.Equal(GameEventAppendStatus.AlreadyPresent, second.Status);
    Assert.Equal(1, second.GameState.Revision);
    Assert.Equal(1, await ScalarIntAsync(database.AnchorConnection, "SELECT COUNT(*) FROM game_events;"));
  }

  [Fact]
  public async Task Append_rejects_event_id_reused_with_different_payload()
  {
    await using var database = await TestDatabase.CreateAsync();
    var started = Started(1, PlayerSeat.Self);
    await database.Journal.AppendAsync(started);
    var conflicting = new RoundStartedEvent(
      started.EventId,
      started.SessionId,
      started.Sequence,
      started.OccurredAtUtc,
      started.Provenance,
      started.RuleProfileVersion,
      PlayerSeat.Next);

    await Assert.ThrowsAsync<GameEventJournalConflictException>(() =>
      database.Journal.AppendAsync(conflicting));
  }

  [Fact]
  public async Task Append_rejects_out_of_order_event_without_partial_write()
  {
    await using var database = await TestDatabase.CreateAsync();
    await database.Journal.AppendAsync(Started(1, PlayerSeat.Self));
    var outOfOrder = new TurnChangedEvent(
      EventId(),
      SessionId,
      3,
      OccurredAtUtc.AddSeconds(3),
      GameEventProvenance.System,
      PlayerSeat.Next);

    await Assert.ThrowsAsync<GameEventJournalConflictException>(() =>
      database.Journal.AppendAsync(outOfOrder));

    Assert.Equal(1, await ScalarIntAsync(database.AnchorConnection, "SELECT COUNT(*) FROM game_events;"));
    Assert.Equal(1, await ScalarIntAsync(
      database.AnchorConnection,
      "SELECT last_event_sequence FROM game_sessions;"));
  }

  [Fact]
  public async Task Concurrent_same_sequence_appends_are_serialized_and_only_one_wins()
  {
    await using var database = await TestDatabase.CreateAsync();
    await database.Journal.AppendAsync(Started(1, PlayerSeat.Self));
    var first = new TileDiscardedEvent(
      EventId(), SessionId, 2, OccurredAtUtc.AddSeconds(2), GameEventProvenance.System,
      PlayerSeat.Self, TileType.Parse("5p"));
    var second = new TileDiscardedEvent(
      EventId(), SessionId, 2, OccurredAtUtc.AddSeconds(2), GameEventProvenance.System,
      PlayerSeat.Self, TileType.Parse("6p"));

    var outcomes = await Task.WhenAll(AppendOutcomeAsync(first), AppendOutcomeAsync(second));

    Assert.Equal(1, outcomes.Count(outcome => outcome == "appended"));
    Assert.Equal(1, outcomes.Count(outcome => outcome == "conflict"));
    Assert.Equal(2, await ScalarIntAsync(database.AnchorConnection, "SELECT COUNT(*) FROM game_events;"));

    async Task<string> AppendOutcomeAsync(GameEvent gameEvent)
    {
      try
      {
        await database.Journal.AppendAsync(gameEvent);
        return "appended";
      }
      catch (GameEventJournalConflictException)
      {
        return "conflict";
      }
    }
  }

  [Fact]
  public async Task Recover_ignores_corrupt_snapshot_and_replays_authoritative_events()
  {
    await using var database = await TestDatabase.CreateAsync(snapshotInterval: 2);
    await database.Journal.AppendAsync(Started(1, PlayerSeat.Self));
    await database.Journal.AppendAsync(SelfHand(2));
    await ExecuteAsync(
      database.AnchorConnection,
      "UPDATE game_state_snapshots SET state_json = '{\"bad\":true}';");

    var restored = await database.Journal.RecoverAsync(SessionId);

    Assert.NotNull(restored);
    Assert.Equal(2, restored.Revision);
    Assert.Equal(14, restored.GetPlayer(PlayerSeat.Self).KnownConcealedTiles.Length);
  }

  [Fact]
  public async Task Recover_rejects_corrupt_authoritative_event_document()
  {
    await using var database = await TestDatabase.CreateAsync(snapshotInterval: 0);
    await database.Journal.AppendAsync(Started(1, PlayerSeat.Self));
    await ExecuteAsync(
      database.AnchorConnection,
      "UPDATE game_events SET event_json = '{}';");

    await Assert.ThrowsAsync<GameEventJournalCorruptionException>(() =>
      database.Journal.RecoverAsync(SessionId));
  }

  [Fact]
  public async Task Recover_does_not_allow_snapshot_to_hide_corrupt_event_prefix()
  {
    await using var database = await TestDatabase.CreateAsync(snapshotInterval: 2);
    await database.Journal.AppendAsync(Started(1, PlayerSeat.Self));
    await database.Journal.AppendAsync(SelfHand(2));
    await ExecuteAsync(
      database.AnchorConnection,
      "UPDATE game_events SET event_json = '{}' WHERE sequence = 1;");

    await Assert.ThrowsAsync<GameEventJournalCorruptionException>(() =>
      database.Journal.RecoverAsync(SessionId));
  }

  [Fact]
  public async Task Completed_session_is_snapshotted_and_not_listed_as_active()
  {
    await using var database = await TestDatabase.CreateAsync(snapshotInterval: 0);
    await database.Journal.AppendAsync(Started(1, PlayerSeat.Self));
    await database.Journal.AppendAsync(new RoundEndedEvent(
      EventId(),
      SessionId,
      2,
      OccurredAtUtc.AddSeconds(2),
      GameEventProvenance.System,
      RoundEndReason.Draw));

    Assert.Empty(await database.Journal.ListActiveSessionIdsAsync());
    Assert.Equal(1, await ScalarIntAsync(
      database.AnchorConnection,
      "SELECT COUNT(*) FROM game_state_snapshots WHERE revision = 2;"));
  }

  private GameEvent[] CompleteInProgressPrefix() =>
  [
    Started(1, PlayerSeat.Self),
    SelfHand(2),
    new TileDiscardedEvent(
      EventId(), SessionId, 3, OccurredAtUtc.AddSeconds(3), GameEventProvenance.System,
      PlayerSeat.Self, TileType.Parse("5p")),
    new TurnChangedEvent(
      EventId(), SessionId, 4, OccurredAtUtc.AddSeconds(4), GameEventProvenance.System,
      PlayerSeat.Next),
    new TileDrawnEvent(
      EventId(), SessionId, 5, OccurredAtUtc.AddSeconds(5), GameEventProvenance.System,
      PlayerSeat.Next, null),
    new TileDiscardedEvent(
      EventId(), SessionId, 6, OccurredAtUtc.AddSeconds(6), GameEventProvenance.System,
      PlayerSeat.Next, TileType.Parse("9s")),
    new IndicatorRevealedEvent(
      EventId(), SessionId, 7, OccurredAtUtc.AddSeconds(7), GameEventProvenance.System,
      IndicatorKind.Wildcard, TileType.Parse("6p"))
  ];

  private RoundStartedEvent Started(long sequence, PlayerSeat dealer) => new(
    EventId(),
    SessionId,
    sequence,
    OccurredAtUtc.AddSeconds(sequence),
    GameEventProvenance.System,
    "wuhan-v1",
    dealer);

  private SelfHandSnapshotConfirmedEvent SelfHand(long sequence) => new(
    EventId(),
    SessionId,
    sequence,
    OccurredAtUtc.AddSeconds(sequence),
    GameEventProvenance.System,
    InitialDealerHand().Select(TileType.Parse),
    0);

  private Guid EventId() => Guid.Parse($"00000000-0000-0000-0000-{++eventCounter:000000000000}");

  private static string[] InitialDealerHand() =>
    ["1m", "2m", "3m", "4m", "5m", "6m", "7m", "8m", "9m", "1p", "2p", "3p", "4p", "5p"];

  private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql)
  {
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt32(await command.ExecuteScalarAsync());
  }

  private static async Task ExecuteAsync(SqliteConnection connection, string sql)
  {
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
  }

  private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => value;
  }

  private sealed class TestDatabase : IAsyncDisposable
  {
    private TestDatabase(
      SqliteConnection anchorConnection,
      SqliteConnectionFactory connectionFactory,
      SqliteGameEventJournal journal)
    {
      AnchorConnection = anchorConnection;
      ConnectionFactory = connectionFactory;
      Journal = journal;
    }

    public SqliteConnection AnchorConnection { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public SqliteGameEventJournal Journal { get; }

    public static async Task<TestDatabase> CreateAsync(int snapshotInterval = 25)
    {
      var databaseName = $"game-journal-{Guid.NewGuid():N}";
      var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
      var anchorConnection = new SqliteConnection(connectionString);
      await anchorConnection.OpenAsync();
      var connectionFactory = new SqliteConnectionFactory(connectionString);
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync();
      return new TestDatabase(
        anchorConnection,
        connectionFactory,
        new SqliteGameEventJournal(
          connectionFactory,
          timeProvider: new FixedTimeProvider(OccurredAtUtc.AddHours(1)),
          snapshotInterval: snapshotInterval));
    }

    public async ValueTask DisposeAsync() => await AnchorConnection.DisposeAsync();
  }
}
