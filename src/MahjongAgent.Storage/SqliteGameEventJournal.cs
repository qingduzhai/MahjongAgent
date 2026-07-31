using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using MahjongAgent.Core.Gameplay;
using Microsoft.Data.Sqlite;

namespace MahjongAgent.Storage;

public sealed class SqliteGameEventJournal
{
  private readonly SqliteConnectionFactory connectionFactory;
  private readonly GameStateReducer reducer;
  private readonly TimeProvider timeProvider;
  private readonly int snapshotInterval;
  private readonly SemaphoreSlim writeGate = new(1, 1);

  public SqliteGameEventJournal(
    SqliteConnectionFactory connectionFactory,
    GameStateReducer? reducer = null,
    TimeProvider? timeProvider = null,
    int snapshotInterval = 25)
  {
    ArgumentNullException.ThrowIfNull(connectionFactory);
    if (snapshotInterval is < 0 or > 1000)
    {
      throw new ArgumentOutOfRangeException(
        nameof(snapshotInterval),
        "Snapshot interval must be zero (disabled) or between one and one thousand.");
    }

    this.connectionFactory = connectionFactory;
    this.reducer = reducer ?? new GameStateReducer();
    this.timeProvider = timeProvider ?? TimeProvider.System;
    this.snapshotInterval = snapshotInterval;
  }

  public async Task<GameEventAppendResult> AppendAsync(
    GameEvent gameEvent,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(gameEvent);
    await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      return await AppendCoreAsync(gameEvent, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      writeGate.Release();
    }
  }

  private async Task<GameEventAppendResult> AppendCoreAsync(
    GameEvent gameEvent,
    CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    await using var transaction = await connection
      .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
      .ConfigureAwait(false);

    try
    {
      var existing = await FindEventAsync(
        connection,
        transaction,
        gameEvent.EventId,
        cancellationToken).ConfigureAwait(false);
      if (existing is not null)
      {
        ValidateIdempotentMatch(existing, gameEvent);
        var current = await RestoreRequiredAsync(
          connection,
          transaction,
          gameEvent.SessionId,
          cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new GameEventAppendResult(GameEventAppendStatus.AlreadyPresent, current);
      }

      var session = await FindSessionAsync(
        connection,
        transaction,
        gameEvent.SessionId,
        cancellationToken).ConfigureAwait(false);
      var currentState = session is null
        ? GameState.Empty
        : await RestoreRequiredAsync(
          connection,
          transaction,
          gameEvent.SessionId,
          cancellationToken).ConfigureAwait(false);
      GameState nextState;
      try
      {
        nextState = reducer.Apply(currentState, gameEvent);
      }
      catch (GameStateTransitionException exception)
      {
        throw new GameEventJournalConflictException(
          "The game event cannot be appended to the confirmed journal state.",
          exception);
      }

      var storedAtUtc = timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
      if (session is null)
      {
        if (gameEvent is not RoundStartedEvent)
        {
          throw new GameEventJournalConflictException(
            "A new journal session must start with RoundStarted.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
          """
          INSERT INTO game_sessions (
            session_id,
            rule_profile_version,
            phase,
            last_event_sequence,
            last_event_fingerprint,
            created_at_utc,
            updated_at_utc,
            ended_at_utc)
          VALUES (
            @SessionId,
            @RuleProfileVersion,
            @Phase,
            @LastEventSequence,
            @LastEventFingerprint,
            @CreatedAtUtc,
            @UpdatedAtUtc,
            @EndedAtUtc);
          """,
          SessionParameters(nextState, storedAtUtc, storedAtUtc),
          transaction,
          cancellationToken: cancellationToken)).ConfigureAwait(false);
      }
      else
      {
        var updated = await connection.ExecuteAsync(new CommandDefinition(
          """
          UPDATE game_sessions
          SET phase = @Phase,
              last_event_sequence = @LastEventSequence,
              last_event_fingerprint = @LastEventFingerprint,
              updated_at_utc = @UpdatedAtUtc,
              ended_at_utc = @EndedAtUtc
          WHERE session_id = @SessionId
            AND last_event_sequence = @ExpectedPreviousSequence
            AND last_event_fingerprint = @ExpectedPreviousFingerprint;
          """,
          new
          {
            SessionId = nextState.SessionId.ToString("D"),
            Phase = (int)nextState.Phase,
            LastEventSequence = nextState.Revision,
            LastEventFingerprint = nextState.LastEventFingerprint,
            UpdatedAtUtc = storedAtUtc,
            EndedAtUtc = nextState.Phase is GameSessionPhase.Completed ? storedAtUtc : null,
            ExpectedPreviousSequence = currentState.Revision,
            ExpectedPreviousFingerprint = currentState.LastEventFingerprint
          },
          transaction,
          cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated != 1)
        {
          throw new GameEventJournalConflictException(
            "The journal changed concurrently while appending the event.");
        }
      }

      var eventJson = GameEventDocumentCodec.Serialize(gameEvent);
      await connection.ExecuteAsync(new CommandDefinition(
        """
        INSERT INTO game_events (
          session_id,
          sequence,
          event_id,
          event_kind,
          event_fingerprint,
          event_document_checksum,
          occurred_at_utc,
          source_kind,
          confidence,
          event_json,
          stored_at_utc)
        VALUES (
          @SessionId,
          @Sequence,
          @EventId,
          @EventKind,
          @EventFingerprint,
          @EventDocumentChecksum,
          @OccurredAtUtc,
          @SourceKind,
          @Confidence,
          @EventJson,
          @StoredAtUtc);
        """,
        new
        {
          SessionId = gameEvent.SessionId.ToString("D"),
          gameEvent.Sequence,
          EventId = gameEvent.EventId.ToString("D"),
          EventKind = gameEvent.Kind.ToProtocolValue(),
          EventFingerprint = gameEvent.Fingerprint,
          EventDocumentChecksum = ComputeChecksum(eventJson),
          OccurredAtUtc = gameEvent.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
          SourceKind = (int)gameEvent.Provenance.SourceKind,
          gameEvent.Provenance.Confidence,
          EventJson = eventJson,
          StoredAtUtc = storedAtUtc
        },
        transaction,
        cancellationToken: cancellationToken)).ConfigureAwait(false);

      if (ShouldSnapshot(nextState))
      {
        await SaveSnapshotAsync(
          connection,
          transaction,
          nextState,
          storedAtUtc,
          cancellationToken).ConfigureAwait(false);
      }

      await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
      return new GameEventAppendResult(GameEventAppendStatus.Appended, nextState);
    }
    catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
    {
      await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
      return await ResolveConstraintConflictAsync(gameEvent, exception, cancellationToken)
        .ConfigureAwait(false);
    }
    catch
    {
      await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
      throw;
    }
  }

  public async Task<GameState?> RecoverAsync(
    Guid sessionId,
    CancellationToken cancellationToken = default)
  {
    if (sessionId == Guid.Empty)
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    await using var transaction = await connection
      .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
      .ConfigureAwait(false);
    var session = await FindSessionAsync(connection, transaction, sessionId, cancellationToken)
      .ConfigureAwait(false);
    var state = session is null
      ? null
      : await RestoreRequiredAsync(connection, transaction, sessionId, cancellationToken)
        .ConfigureAwait(false);
    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    return state;
  }

  public async Task<IReadOnlyList<Guid>> ListActiveSessionIdsAsync(
    CancellationToken cancellationToken = default)
  {
    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    var values = await connection.QueryAsync<string>(new CommandDefinition(
      """
      SELECT session_id
      FROM game_sessions
      WHERE phase = @Phase
      ORDER BY updated_at_utc DESC;
      """,
      new { Phase = (int)GameSessionPhase.InProgress },
      cancellationToken: cancellationToken)).ConfigureAwait(false);
    try
    {
      return values.Select(Guid.Parse).ToArray();
    }
    catch (FormatException exception)
    {
      throw new GameEventJournalCorruptionException(
        "The active game session catalog contains an invalid session ID.",
        exception);
    }
  }

  private async Task<GameState> RestoreRequiredAsync(
    SqliteConnection connection,
    IDbTransaction? transaction,
    Guid sessionId,
    CancellationToken cancellationToken)
  {
    var session = await FindSessionAsync(connection, transaction, sessionId, cancellationToken)
      .ConfigureAwait(false) ??
      throw new GameEventJournalCorruptionException(
        $"Game session '{sessionId}' was not found.");
    var state = await TryLoadSnapshotAsync(
      connection,
      transaction,
      sessionId,
      cancellationToken).ConfigureAwait(false) ?? GameState.Empty;
    var documents = await connection.QueryAsync<EventDocumentRow>(new CommandDefinition(
      """
      SELECT sequence AS Sequence,
             event_fingerprint AS EventFingerprint,
             event_document_checksum AS EventDocumentChecksum,
             event_json AS EventJson
      FROM game_events
      WHERE session_id = @SessionId AND sequence > @Revision
      ORDER BY sequence;
      """,
      new
      {
        SessionId = sessionId.ToString("D"),
        Revision = state.Revision
      },
      transaction,
      cancellationToken: cancellationToken)).ConfigureAwait(false);

    try
    {
      foreach (var document in documents)
      {
        if (!string.Equals(
              ComputeChecksum(document.EventJson),
              document.EventDocumentChecksum,
              StringComparison.Ordinal))
        {
          throw new GameEventJournalCorruptionException(
            "Stored event document checksum verification failed.");
        }

        var gameEvent = GameEventDocumentCodec.Deserialize(document.EventJson);
        if (gameEvent.Sequence != document.Sequence ||
            !string.Equals(
              gameEvent.Fingerprint,
              document.EventFingerprint,
              StringComparison.Ordinal))
        {
          throw new GameEventJournalCorruptionException(
            "Stored event sequence does not match its document.");
        }

        state = reducer.Apply(state, gameEvent);
      }
    }
    catch (GameEventJournalCorruptionException)
    {
      throw;
    }
    catch (Exception exception) when (
      exception is GameEventDocumentException or GameStateTransitionException)
    {
      throw new GameEventJournalCorruptionException(
        $"Game session '{sessionId}' contains an invalid event stream.",
        exception);
    }

    if (state.Revision != session.LastEventSequence ||
        !string.Equals(
          state.LastEventFingerprint,
          session.LastEventFingerprint,
          StringComparison.Ordinal) ||
        (int)state.Phase != session.Phase ||
        !string.Equals(
          state.RuleProfileVersion,
          session.RuleProfileVersion,
          StringComparison.Ordinal))
    {
      throw new GameEventJournalCorruptionException(
        $"Game session '{sessionId}' metadata does not match its event stream.");
    }

    return state;
  }

  private async Task<GameState?> TryLoadSnapshotAsync(
    SqliteConnection connection,
    IDbTransaction? transaction,
    Guid sessionId,
    CancellationToken cancellationToken)
  {
    var snapshots = await connection.QueryAsync<SnapshotRow>(new CommandDefinition(
      """
      SELECT revision AS Revision,
             last_event_fingerprint AS LastEventFingerprint,
             state_json AS StateJson
      FROM game_state_snapshots
      WHERE session_id = @SessionId
      ORDER BY revision DESC;
      """,
      new { SessionId = sessionId.ToString("D") },
      transaction,
      cancellationToken: cancellationToken)).ConfigureAwait(false);

    foreach (var snapshot in snapshots)
    {
      try
      {
        var state = GameStateDocumentCodec.Deserialize(snapshot.StateJson);
        if (state.SessionId != sessionId ||
            state.Revision != snapshot.Revision ||
            !string.Equals(
              state.LastEventFingerprint,
              snapshot.LastEventFingerprint,
              StringComparison.Ordinal))
        {
          continue;
        }

        var eventFingerprint = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
          """
          SELECT event_fingerprint
          FROM game_events
          WHERE session_id = @SessionId AND sequence = @Revision;
          """,
          new
          {
            SessionId = sessionId.ToString("D"),
            snapshot.Revision
          },
          transaction,
          cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (string.Equals(
              eventFingerprint,
              snapshot.LastEventFingerprint,
              StringComparison.Ordinal) &&
            await ValidateEventDocumentsThroughRevisionAsync(
              connection,
              transaction,
              sessionId,
              snapshot.Revision,
              cancellationToken).ConfigureAwait(false))
        {
          return state;
        }
      }
      catch (GameStateDocumentException)
      {
        // A snapshot is a disposable acceleration cache. Try an older snapshot or replay all events.
      }
    }

    return null;
  }

  private async Task SaveSnapshotAsync(
    SqliteConnection connection,
    IDbTransaction transaction,
    GameState state,
    string createdAtUtc,
    CancellationToken cancellationToken)
  {
    await connection.ExecuteAsync(new CommandDefinition(
      """
      INSERT INTO game_state_snapshots (
        session_id,
        revision,
        last_event_fingerprint,
        state_json,
        created_at_utc)
      VALUES (
        @SessionId,
        @Revision,
        @LastEventFingerprint,
        @StateJson,
        @CreatedAtUtc)
      ON CONFLICT(session_id, revision) DO UPDATE SET
        last_event_fingerprint = excluded.last_event_fingerprint,
        state_json = excluded.state_json,
        created_at_utc = excluded.created_at_utc;
      """,
      new
      {
        SessionId = state.SessionId.ToString("D"),
        state.Revision,
        state.LastEventFingerprint,
        StateJson = GameStateDocumentCodec.Serialize(state),
        CreatedAtUtc = createdAtUtc
      },
      transaction,
      cancellationToken: cancellationToken)).ConfigureAwait(false);
  }

  private static async Task<bool> ValidateEventDocumentsThroughRevisionAsync(
    SqliteConnection connection,
    IDbTransaction? transaction,
    Guid sessionId,
    long revision,
    CancellationToken cancellationToken)
  {
    var documents = await connection.QueryAsync<EventChecksumRow>(new CommandDefinition(
      """
      SELECT sequence AS Sequence,
             event_document_checksum AS EventDocumentChecksum,
             event_json AS EventJson
      FROM game_events
      WHERE session_id = @SessionId AND sequence <= @Revision
      ORDER BY sequence;
      """,
      new
      {
        SessionId = sessionId.ToString("D"),
        Revision = revision
      },
      transaction,
      cancellationToken: cancellationToken)).ConfigureAwait(false);
    long expectedSequence = 1;
    foreach (var document in documents)
    {
      if (document.Sequence != expectedSequence ||
          !string.Equals(
            ComputeChecksum(document.EventJson),
            document.EventDocumentChecksum,
            StringComparison.Ordinal))
      {
        return false;
      }

      expectedSequence++;
    }

    return expectedSequence == revision + 1;
  }

  private bool ShouldSnapshot(GameState state) =>
    state.Phase is GameSessionPhase.Completed ||
    (snapshotInterval > 0 && state.Revision % snapshotInterval == 0);

  private async Task<GameEventAppendResult> ResolveConstraintConflictAsync(
    GameEvent gameEvent,
    SqliteException exception,
    CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    await using var transaction = await connection
      .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
      .ConfigureAwait(false);
    var existing = await FindEventAsync(
      connection,
      transaction,
      gameEvent.EventId,
      cancellationToken).ConfigureAwait(false);
    if (existing is null)
    {
      throw new GameEventJournalConflictException(
        "A concurrent journal write violated session sequence constraints.",
        exception);
    }

    ValidateIdempotentMatch(existing, gameEvent);
    var state = await RestoreRequiredAsync(
      connection,
      transaction,
      gameEvent.SessionId,
      cancellationToken).ConfigureAwait(false);
    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    return new GameEventAppendResult(GameEventAppendStatus.AlreadyPresent, state);
  }

  private static void ValidateIdempotentMatch(EventIdentityRow existing, GameEvent gameEvent)
  {
    if (!string.Equals(existing.SessionId, gameEvent.SessionId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        existing.Sequence != gameEvent.Sequence ||
        !string.Equals(existing.EventFingerprint, gameEvent.Fingerprint, StringComparison.Ordinal))
    {
      throw new GameEventJournalConflictException(
        "An event ID was reused with a different session, sequence, or payload.");
    }
  }

  private static Task<EventIdentityRow?> FindEventAsync(
    SqliteConnection connection,
    IDbTransaction? transaction,
    Guid eventId,
    CancellationToken cancellationToken) =>
    connection.QuerySingleOrDefaultAsync<EventIdentityRow>(new CommandDefinition(
      """
      SELECT session_id AS SessionId,
             sequence AS Sequence,
             event_fingerprint AS EventFingerprint
      FROM game_events
      WHERE event_id = @EventId;
      """,
      new { EventId = eventId.ToString("D") },
      transaction,
      cancellationToken: cancellationToken));

  private static Task<SessionRow?> FindSessionAsync(
    SqliteConnection connection,
    IDbTransaction? transaction,
    Guid sessionId,
    CancellationToken cancellationToken) =>
    connection.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition(
      """
      SELECT session_id AS SessionId,
             rule_profile_version AS RuleProfileVersion,
             phase AS Phase,
             last_event_sequence AS LastEventSequence,
             last_event_fingerprint AS LastEventFingerprint
      FROM game_sessions
      WHERE session_id = @SessionId;
      """,
      new { SessionId = sessionId.ToString("D") },
      transaction,
      cancellationToken: cancellationToken));

  private static object SessionParameters(
    GameState state,
    string createdAtUtc,
    string updatedAtUtc) => new
    {
      SessionId = state.SessionId.ToString("D"),
      state.RuleProfileVersion,
      Phase = (int)state.Phase,
      LastEventSequence = state.Revision,
      state.LastEventFingerprint,
      CreatedAtUtc = createdAtUtc,
      UpdatedAtUtc = updatedAtUtc,
      EndedAtUtc = state.Phase is GameSessionPhase.Completed ? updatedAtUtc : null
    };

  private static string ComputeChecksum(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

  private sealed class EventIdentityRow
  {
    public string SessionId { get; init; } = string.Empty;

    public long Sequence { get; init; }

    public string EventFingerprint { get; init; } = string.Empty;
  }

  private sealed class EventDocumentRow
  {
    public long Sequence { get; init; }

    public string EventFingerprint { get; init; } = string.Empty;

    public string EventDocumentChecksum { get; init; } = string.Empty;

    public string EventJson { get; init; } = string.Empty;
  }

  private sealed class EventChecksumRow
  {
    public long Sequence { get; init; }

    public string EventDocumentChecksum { get; init; } = string.Empty;

    public string EventJson { get; init; } = string.Empty;
  }

  private sealed class SnapshotRow
  {
    public long Revision { get; init; }

    public string LastEventFingerprint { get; init; } = string.Empty;

    public string StateJson { get; init; } = string.Empty;
  }

  private sealed class SessionRow
  {
    public string SessionId { get; init; } = string.Empty;

    public string RuleProfileVersion { get; init; } = string.Empty;

    public int Phase { get; init; }

    public long LastEventSequence { get; init; }

    public string LastEventFingerprint { get; init; } = string.Empty;
  }
}
