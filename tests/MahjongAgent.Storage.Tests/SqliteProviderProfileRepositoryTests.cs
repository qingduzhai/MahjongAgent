using MahjongAgent.Providers.OpenAICompatible;
using Microsoft.Data.Sqlite;

namespace MahjongAgent.Storage.Tests;

public sealed class SqliteProviderProfileRepositoryTests
{
  [Fact]
  public async Task Migration_is_idempotent()
  {
    await using var database = await TestDatabase.CreateAsync();

    await database.MigrationRunner.ApplyAsync();
    await database.MigrationRunner.ApplyAsync();

    await using var command = database.AnchorConnection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
    var migrationCount = Convert.ToInt32(await command.ExecuteScalarAsync());
    Assert.Equal(2, migrationCount);
  }

  [Fact]
  public async Task Repository_round_trips_profile_and_probe_without_api_key_value()
  {
    await using var database = await TestDatabase.CreateAsync();
    await database.MigrationRunner.ApplyAsync();
    var profile = CreateProfile();

    await database.Repository.UpsertAsync(profile);
    var restored = await database.Repository.GetAsync(profile.Id);

    Assert.NotNull(restored);
    Assert.Equal(profile.Id, restored.Id);
    Assert.Equal(profile.DisplayName, restored.DisplayName);
    Assert.Equal(profile.BaseUri, restored.BaseUri);
    Assert.Equal(profile.CredentialId, restored.CredentialId);
    Assert.Equal(ApiKeyTransport.Header, restored.ApiKeyTransport);
    Assert.Equal("X-Api-Key", restored.ApiKeyHeaderName);
    Assert.Equal("tenant-a", restored.AdditionalHeaders["X-Tenant"]);
    Assert.Equal("vision-model-actual", restored.LastProbe?.ActualModel);
    Assert.Equal(125, restored.LastProbe?.DurationMilliseconds);

    var propertyNames = typeof(OpenAiCompatibleProviderProfile)
      .GetProperties()
      .Select(property => property.Name)
      .ToArray();
    Assert.DoesNotContain("ApiKey", propertyNames);
    Assert.DoesNotContain("Secret", propertyNames);

    await using var command = database.AnchorConnection.CreateCommand();
    command.CommandText = "SELECT group_concat(name, ',') FROM pragma_table_info('openai_compatible_provider_profiles');";
    var columnNames = Convert.ToString(await command.ExecuteScalarAsync());
    Assert.DoesNotContain("api_key_value", columnNames, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("secret", columnNames, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task Repository_lists_updates_and_deletes_profiles()
  {
    await using var database = await TestDatabase.CreateAsync();
    await database.MigrationRunner.ApplyAsync();
    var first = CreateProfile() with { Id = "provider-b", DisplayName = "Zulu" };
    var second = CreateProfile() with { Id = "provider-a", DisplayName = "Alpha" };
    await database.Repository.UpsertAsync(first);
    await database.Repository.UpsertAsync(second);

    var profiles = await database.Repository.ListAsync();
    Assert.Equal(["provider-a", "provider-b"], profiles.Select(profile => profile.Id));

    await database.Repository.UpsertAsync(second with
    {
      DisplayName = "Updated",
      LastProbe = new ProviderProbeSnapshot
      {
        ProbedAtUtc = DateTimeOffset.UtcNow,
        IsSuccess = false,
        FailureKind = ProviderProbeFailureKind.Authentication
      }
    });
    var updated = await database.Repository.GetAsync(second.Id);
    Assert.Equal("Updated", updated?.DisplayName);
    Assert.False(updated?.LastProbe?.IsSuccess);
    Assert.Equal(ProviderProbeFailureKind.Authentication, updated?.LastProbe?.FailureKind);

    Assert.True(await database.Repository.DeleteAsync(first.Id));
    Assert.False(await database.Repository.DeleteAsync(first.Id));
    Assert.Null(await database.Repository.GetAsync(first.Id));
  }

  private static OpenAiCompatibleProviderProfile CreateProfile() => new()
  {
    Id = "provider-test",
    DisplayName = "Provider Test",
    BaseUri = new Uri("https://provider.example/v1/"),
    ChatCompletionsPath = "chat/completions",
    PerceptionModel = "vision-model",
    CredentialId = "mahjong-agent/provider/provider-test",
    ApiKeyTransport = ApiKeyTransport.Header,
    ApiKeyHeaderName = "X-Api-Key",
    ApiKeyPrefix = "Token ",
    Capabilities =
      OpenAiCompatibleCapabilities.Vision |
      OpenAiCompatibleCapabilities.JsonSchema,
    StructuredOutputMode = StructuredOutputMode.JsonSchema,
    ImageDetail = ImageDetail.High,
    AdditionalHeaders = new Dictionary<string, string>
    {
      ["X-Tenant"] = "tenant-a"
    },
    LastProbe = new ProviderProbeSnapshot
    {
      ProbedAtUtc = DateTimeOffset.UtcNow,
      IsSuccess = true,
      FailureKind = ProviderProbeFailureKind.None,
      VerifiedCapabilities =
        OpenAiCompatibleCapabilities.Vision |
        OpenAiCompatibleCapabilities.JsonSchema,
      ActualModel = "vision-model-actual",
      DurationMilliseconds = 125,
      ProviderRequestId = "request-id"
    }
  };

  private sealed class TestDatabase : IAsyncDisposable
  {
    private TestDatabase(
      SqliteConnection anchorConnection,
      SqliteMigrationRunner migrationRunner,
      SqliteOpenAiCompatibleProviderProfileRepository repository)
    {
      AnchorConnection = anchorConnection;
      MigrationRunner = migrationRunner;
      Repository = repository;
    }

    public SqliteConnection AnchorConnection { get; }

    public SqliteMigrationRunner MigrationRunner { get; }

    public SqliteOpenAiCompatibleProviderProfileRepository Repository { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
      var databaseName = $"profiles-{Guid.NewGuid():N}";
      var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
      var anchorConnection = new SqliteConnection(connectionString);
      await anchorConnection.OpenAsync();
      var connectionFactory = new SqliteConnectionFactory(connectionString);
      return new TestDatabase(
        anchorConnection,
        new SqliteMigrationRunner(connectionFactory),
        new SqliteOpenAiCompatibleProviderProfileRepository(connectionFactory));
    }

    public async ValueTask DisposeAsync()
    {
      await AnchorConnection.DisposeAsync();
    }
  }
}
