using Dapper;
using Microsoft.Data.Sqlite;

namespace MahjongAgent.Storage;

public sealed class SqliteConnectionFactory
{
  private readonly string connectionString;

  public SqliteConnectionFactory(string connectionString)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
    this.connectionString = connectionString;
  }

  public async Task<SqliteConnection> OpenConnectionAsync(
    CancellationToken cancellationToken = default)
  {
    var connection = new SqliteConnection(connectionString);
    try
    {
      await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
      await connection.ExecuteAsync(new CommandDefinition(
        "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;",
        cancellationToken: cancellationToken)).ConfigureAwait(false);
      return connection;
    }
    catch
    {
      await connection.DisposeAsync().ConfigureAwait(false);
      throw;
    }
  }
}
