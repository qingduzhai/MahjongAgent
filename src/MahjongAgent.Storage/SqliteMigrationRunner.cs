using System.Globalization;
using System.Reflection;
using Dapper;

namespace MahjongAgent.Storage;

public sealed class SqliteMigrationRunner
{
  private const string MigrationResourceSegment = ".Migrations.";
  private readonly SqliteConnectionFactory connectionFactory;
  private readonly Assembly migrationAssembly;

  public SqliteMigrationRunner(
    SqliteConnectionFactory connectionFactory,
    Assembly? migrationAssembly = null)
  {
    ArgumentNullException.ThrowIfNull(connectionFactory);
    this.connectionFactory = connectionFactory;
    this.migrationAssembly = migrationAssembly ?? typeof(SqliteMigrationRunner).Assembly;
  }

  public async Task ApplyAsync(CancellationToken cancellationToken = default)
  {
    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);

    await connection.ExecuteAsync(new CommandDefinition(
      "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;",
      cancellationToken: cancellationToken)).ConfigureAwait(false);

    await connection.ExecuteAsync(new CommandDefinition(
      """
      CREATE TABLE IF NOT EXISTS schema_migrations (
        migration_id TEXT PRIMARY KEY NOT NULL,
        applied_at_utc TEXT NOT NULL
      );
      """,
      cancellationToken: cancellationToken)).ConfigureAwait(false);

    var appliedMigrations = (await connection.QueryAsync<string>(new CommandDefinition(
      "SELECT migration_id FROM schema_migrations;",
      cancellationToken: cancellationToken)).ConfigureAwait(false))
      .ToHashSet(StringComparer.Ordinal);

    var migrations = migrationAssembly
      .GetManifestResourceNames()
      .Where(name => name.Contains(MigrationResourceSegment, StringComparison.Ordinal) &&
                     name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
      .Select(name => new MigrationResource(GetMigrationId(name), name))
      .OrderBy(migration => migration.Id, StringComparer.Ordinal)
      .ToArray();

    foreach (var migration in migrations)
    {
      if (appliedMigrations.Contains(migration.Id))
      {
        continue;
      }

      var sql = await ReadMigrationAsync(migration.ResourceName, cancellationToken)
        .ConfigureAwait(false);
      await using var transaction = await connection
        .BeginTransactionAsync(cancellationToken)
        .ConfigureAwait(false);
      try
      {
        await connection.ExecuteAsync(new CommandDefinition(
          sql,
          transaction: transaction,
          cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
          "INSERT INTO schema_migrations (migration_id, applied_at_utc) VALUES (@Id, @AppliedAtUtc);",
          new
          {
            migration.Id,
            AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
          },
          transaction,
          cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        throw;
      }
    }
  }

  private async Task<string> ReadMigrationAsync(
    string resourceName,
    CancellationToken cancellationToken)
  {
    await using var stream = migrationAssembly.GetManifestResourceStream(resourceName) ??
      throw new InvalidOperationException($"Migration resource '{resourceName}' was not found.");
    using var reader = new StreamReader(stream);
    return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
  }

  private static string GetMigrationId(string resourceName)
  {
    var segmentIndex = resourceName.LastIndexOf(MigrationResourceSegment, StringComparison.Ordinal);
    if (segmentIndex < 0)
    {
      throw new InvalidOperationException($"Resource '{resourceName}' is not a migration.");
    }

    return resourceName[(segmentIndex + MigrationResourceSegment.Length)..^4];
  }

  private sealed record MigrationResource(string Id, string ResourceName);
}
