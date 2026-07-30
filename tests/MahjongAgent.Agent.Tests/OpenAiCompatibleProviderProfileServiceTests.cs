using MahjongAgent.Agent.Providers;
using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Agent.Tests;

public sealed class OpenAiCompatibleProviderProfileServiceTests
{
  [Fact]
  public async Task TestAndSaveAsync_probes_before_saving_new_profile_and_key()
  {
    var repository = new FakeProfileRepository();
    var credentialStore = new FakeCredentialStore();
    var probeRunner = new FakeProbeRunner(SuccessfulProbe());
    var service = CreateService(repository, credentialStore, probeRunner);
    var profile = CreateProfile();

    var result = await service.TestAndSaveAsync(profile, "candidate-key");

    Assert.True(result.IsSaved);
    Assert.Equal("candidate-key", credentialStore.Secrets[profile.CredentialId!]);
    Assert.True(repository.Profiles[profile.Id].LastProbe?.IsSuccess);
    Assert.Equal("candidate-key", probeRunner.LastCandidateApiKey);
    Assert.Equal(1, probeRunner.CallCount);
  }

  [Fact]
  public async Task TestAndSaveAsync_does_not_mutate_storage_when_probe_fails()
  {
    var repository = new FakeProfileRepository();
    var credentialStore = new FakeCredentialStore();
    var probeRunner = new FakeProbeRunner(ProviderProbeResult.Failure(
      "provider-test",
      ProviderProbeFailureKind.Authentication,
      "invalid key"));
    var service = CreateService(repository, credentialStore, probeRunner);

    var result = await service.TestAndSaveAsync(CreateProfile(), "candidate-key");

    Assert.False(result.IsSaved);
    Assert.Empty(repository.Profiles);
    Assert.Empty(credentialStore.Secrets);
    Assert.False(result.Profile.LastProbe?.IsSuccess);
  }

  [Fact]
  public async Task TestAndSaveAsync_restores_old_key_when_profile_write_fails()
  {
    var existing = CreateProfile();
    var repository = new FakeProfileRepository(existing)
    {
      UpsertException = new IOException("database unavailable")
    };
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" }
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    var exception = await Assert.ThrowsAsync<IOException>(
      () => service.TestAndSaveAsync(existing with { DisplayName = "Updated" }, "new-key"));

    Assert.Equal("database unavailable", exception.Message);
    Assert.Equal("old-key", credentialStore.Secrets[existing.CredentialId!]);
    Assert.Equal(existing.DisplayName, repository.Profiles[existing.Id].DisplayName);
  }

  [Fact]
  public async Task TestAndSaveAsync_moves_key_when_credential_id_changes()
  {
    var existing = CreateProfile();
    var updated = existing with { CredentialId = "mahjong-agent/provider/new-id" };
    var repository = new FakeProfileRepository(existing);
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" }
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    var result = await service.TestAndSaveAsync(updated, "new-key");

    Assert.True(result.IsSaved);
    Assert.False(credentialStore.Secrets.ContainsKey(existing.CredentialId!));
    Assert.Equal("new-key", credentialStore.Secrets[updated.CredentialId!]);
    Assert.Equal(updated.CredentialId, repository.Profiles[updated.Id].CredentialId);
  }

  [Fact]
  public async Task TestAndSaveAsync_rolls_back_profile_and_new_key_when_old_key_delete_fails()
  {
    var existing = CreateProfile();
    var updated = existing with { CredentialId = "mahjong-agent/provider/new-id" };
    var repository = new FakeProfileRepository(existing);
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" },
      DeleteExceptionCredentialId = existing.CredentialId,
      DeleteException = new IOException("credential manager unavailable")
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    var exception = await Assert.ThrowsAsync<IOException>(
      () => service.TestAndSaveAsync(updated, "new-key"));

    Assert.Equal("credential manager unavailable", exception.Message);
    Assert.Equal(existing.CredentialId, repository.Profiles[existing.Id].CredentialId);
    Assert.Equal("old-key", credentialStore.Secrets[existing.CredentialId!]);
    Assert.False(credentialStore.Secrets.ContainsKey(updated.CredentialId!));
  }

  [Fact]
  public async Task TestAndSaveAsync_removes_obsolete_key_for_local_provider()
  {
    var existing = CreateProfile();
    var local = existing with
    {
      BaseUri = new Uri("http://localhost:1234/v1/"),
      ApiKeyTransport = ApiKeyTransport.None,
      CredentialId = null
    };
    var repository = new FakeProfileRepository(existing);
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" }
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    var result = await service.TestAndSaveAsync(local, null);

    Assert.True(result.IsSaved);
    Assert.Empty(credentialStore.Secrets);
    Assert.Equal(ApiKeyTransport.None, repository.Profiles[local.Id].ApiKeyTransport);
  }

  [Fact]
  public async Task TestAndSaveAsync_rejects_shared_credential_before_probe()
  {
    var first = CreateProfile() with { Id = "first" };
    var second = CreateProfile() with { Id = "second" };
    var repository = new FakeProfileRepository(first);
    var probeRunner = new FakeProbeRunner(SuccessfulProbe());
    var service = CreateService(repository, new FakeCredentialStore(), probeRunner);

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => service.TestAndSaveAsync(second, "key"));

    Assert.Equal(0, probeRunner.CallCount);
  }

  [Fact]
  public async Task DeleteAsync_restores_key_when_profile_delete_fails()
  {
    var existing = CreateProfile();
    var repository = new FakeProfileRepository(existing)
    {
      DeleteException = new IOException("database unavailable")
    };
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" }
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(existing.Id));

    Assert.Equal("old-key", credentialStore.Secrets[existing.CredentialId!]);
    Assert.True(repository.Profiles.ContainsKey(existing.Id));
  }

  [Fact]
  public async Task DeleteAsync_removes_profile_and_exclusive_key()
  {
    var existing = CreateProfile();
    var repository = new FakeProfileRepository(existing);
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" }
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    Assert.True(await service.DeleteAsync(existing.Id));
    Assert.Empty(repository.Profiles);
    Assert.Empty(credentialStore.Secrets);
  }

  [Fact]
  public async Task TestAndSaveAsync_compensates_operation_cancellation_after_key_write()
  {
    var existing = CreateProfile();
    using var cancellationSource = new CancellationTokenSource();
    var repository = new FakeProfileRepository(existing)
    {
      UpsertException = new OperationCanceledException(cancellationSource.Token)
    };
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" }
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => service.TestAndSaveAsync(existing, "new-key", cancellationSource.Token));

    Assert.Equal("old-key", credentialStore.Secrets[existing.CredentialId!]);
    Assert.Equal(existing, repository.Profiles[existing.Id]);
  }

  [Fact]
  public async Task TestAndSaveAsync_reports_compensation_failure()
  {
    var existing = CreateProfile();
    var repository = new FakeProfileRepository(existing)
    {
      UpsertException = new IOException("database unavailable")
    };
    var credentialStore = new FakeCredentialStore
    {
      Secrets = { [existing.CredentialId!] = "old-key" },
      SaveExceptionCallNumber = 2,
      SaveException = new IOException("credential restore failed")
    };
    var service = CreateService(
      repository,
      credentialStore,
      new FakeProbeRunner(SuccessfulProbe()));

    var exception = await Assert.ThrowsAsync<ProviderProfileTransactionException>(
      () => service.TestAndSaveAsync(existing, "new-key"));

    Assert.IsType<IOException>(exception.InnerException);
    Assert.Single(exception.CompensationExceptions);
    Assert.Equal("credential restore failed", exception.CompensationExceptions[0].Message);
  }

  private static OpenAiCompatibleProviderProfileService CreateService(
    FakeProfileRepository repository,
    FakeCredentialStore credentialStore,
    FakeProbeRunner probeRunner) =>
    new(repository, credentialStore, probeRunner);

  private static OpenAiCompatibleProviderProfile CreateProfile() => new()
  {
    Id = "provider-test",
    DisplayName = "Provider Test",
    BaseUri = new Uri("https://provider.example/v1/"),
    PerceptionModel = "vision-model",
    CredentialId = "mahjong-agent/provider/shared",
    ApiKeyTransport = ApiKeyTransport.Bearer,
    Capabilities =
      OpenAiCompatibleCapabilities.Vision |
      OpenAiCompatibleCapabilities.JsonSchema,
    StructuredOutputMode = StructuredOutputMode.JsonSchema
  };

  private static ProviderProbeResult SuccessfulProbe() => ProviderProbeResult.Success(
    "provider-test",
    "vision-model-actual",
    "request-id",
    TimeSpan.FromMilliseconds(100),
    OpenAiCompatibleCapabilities.Vision | OpenAiCompatibleCapabilities.JsonSchema);

  private sealed class FakeProbeRunner(ProviderProbeResult result)
    : IOpenAiCompatibleProviderProbeRunner
  {
    public int CallCount { get; private set; }

    public string? LastCandidateApiKey { get; private set; }

    public Task<ProviderProbeResult> ProbeAsync(
      OpenAiCompatibleProviderProfile profile,
      string? candidateApiKey,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      LastCandidateApiKey = candidateApiKey;
      return Task.FromResult(result);
    }
  }

  private sealed class FakeProfileRepository(params OpenAiCompatibleProviderProfile[] profiles)
    : IOpenAiCompatibleProviderProfileRepository
  {
    public Dictionary<string, OpenAiCompatibleProviderProfile> Profiles { get; } =
      profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);

    public Exception? UpsertException { get; set; }

    public Exception? DeleteException { get; set; }

    public Task<IReadOnlyList<OpenAiCompatibleProviderProfile>> ListAsync(
      CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<OpenAiCompatibleProviderProfile>>(Profiles.Values.ToArray());

    public Task<OpenAiCompatibleProviderProfile?> GetAsync(
      string id,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(Profiles.GetValueOrDefault(id));

    public Task UpsertAsync(
      OpenAiCompatibleProviderProfile profile,
      CancellationToken cancellationToken = default)
    {
      if (UpsertException is not null)
      {
        var exception = UpsertException;
        UpsertException = null;
        return Task.FromException(exception);
      }

      Profiles[profile.Id] = profile;
      return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(
      string id,
      CancellationToken cancellationToken = default)
    {
      if (DeleteException is not null)
      {
        var exception = DeleteException;
        DeleteException = null;
        return Task.FromException<bool>(exception);
      }

      return Task.FromResult(Profiles.Remove(id));
    }
  }

  private sealed class FakeCredentialStore : IApiKeyCredentialStore
  {
    public Dictionary<string, string> Secrets { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? DeleteExceptionCredentialId { get; init; }

    public Exception? DeleteException { get; init; }

    public int? SaveExceptionCallNumber { get; init; }

    public Exception? SaveException { get; init; }

    private int saveCallCount;

    public ValueTask<string?> ResolveApiKeyAsync(
      string credentialId,
      CancellationToken cancellationToken = default) =>
      ValueTask.FromResult(Secrets.GetValueOrDefault(credentialId));

    public ValueTask SaveApiKeyAsync(
      string credentialId,
      string apiKey,
      CancellationToken cancellationToken = default)
    {
      saveCallCount++;
      if (SaveExceptionCallNumber == saveCallCount)
      {
        return ValueTask.FromException(SaveException!);
      }

      Secrets[credentialId] = apiKey;
      return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteApiKeyAsync(
      string credentialId,
      CancellationToken cancellationToken = default)
    {
      if (credentialId.Equals(DeleteExceptionCredentialId, StringComparison.OrdinalIgnoreCase))
      {
        return ValueTask.FromException<bool>(DeleteException!);
      }

      return ValueTask.FromResult(Secrets.Remove(credentialId));
    }
  }
}
