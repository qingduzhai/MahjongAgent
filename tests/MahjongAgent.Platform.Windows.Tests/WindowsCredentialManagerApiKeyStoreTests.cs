using MahjongAgent.Platform.Windows.Credentials;

namespace MahjongAgent.Platform.Windows.Tests;

public sealed class WindowsCredentialManagerApiKeyStoreTests
{
  [Fact]
  public async Task Store_saves_resolves_overwrites_and_deletes_key()
  {
    var backend = new FakeCredentialBackend();
    var store = new WindowsCredentialManagerApiKeyStore(backend);

    await store.SaveApiKeyAsync("provider/test", "first-key");
    Assert.Equal("first-key", await store.ResolveApiKeyAsync("provider/test"));
    Assert.Equal("MahjongAgent:provider/test", backend.LastTargetName);

    await store.SaveApiKeyAsync("provider/test", "second-key");
    Assert.Equal("second-key", await store.ResolveApiKeyAsync("provider/test"));

    Assert.True(await store.DeleteApiKeyAsync("provider/test"));
    Assert.False(await store.DeleteApiKeyAsync("provider/test"));
    Assert.Null(await store.ResolveApiKeyAsync("provider/test"));
  }

  [Fact]
  public async Task Store_rejects_oversized_key_without_writing()
  {
    var backend = new FakeCredentialBackend();
    var store = new WindowsCredentialManagerApiKeyStore(backend);

    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await store.SaveApiKeyAsync("provider/test", new string('x', 2561)));

    Assert.Empty(backend.Secrets);
  }

  [Fact]
  public async Task Store_rejects_control_characters_in_key()
  {
    var backend = new FakeCredentialBackend();
    var store = new WindowsCredentialManagerApiKeyStore(backend);

    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await store.SaveApiKeyAsync("provider/test", "key\nvalue"));

    Assert.Empty(backend.Secrets);
  }

  [Theory]
  [InlineData("")]
  [InlineData("provider\r\ninjected")]
  public async Task Store_rejects_invalid_credential_id(string credentialId)
  {
    var store = new WindowsCredentialManagerApiKeyStore(new FakeCredentialBackend());

    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await store.SaveApiKeyAsync(credentialId, "key"));
  }

  [Fact]
  public async Task Store_honors_pre_cancelled_operation()
  {
    using var cancellationSource = new CancellationTokenSource();
    cancellationSource.Cancel();
    var backend = new FakeCredentialBackend();
    var store = new WindowsCredentialManagerApiKeyStore(backend);

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      await store.SaveApiKeyAsync("provider/test", "key", cancellationSource.Token));

    Assert.Empty(backend.Secrets);
  }

  [Fact]
  public async Task Live_credential_manager_round_trip_when_explicitly_enabled()
  {
    if (!string.Equals(
          Environment.GetEnvironmentVariable("MAHJONGAGENT_RUN_WINDOWS_CREDENTIAL_TEST"),
          "1",
          StringComparison.Ordinal))
    {
      return;
    }

    const string credentialId = "tests/credential-manager-smoke";
    var store = new WindowsCredentialManagerApiKeyStore();
    try
    {
      await store.DeleteApiKeyAsync(credentialId);
      await store.SaveApiKeyAsync(credentialId, "synthetic-smoke-test-key");
      Assert.Equal("synthetic-smoke-test-key", await store.ResolveApiKeyAsync(credentialId));
    }
    finally
    {
      await store.DeleteApiKeyAsync(credentialId);
    }
  }

  private sealed class FakeCredentialBackend : IWindowsCredentialBackend
  {
    public Dictionary<string, byte[]> Secrets { get; } = new(StringComparer.Ordinal);

    public string? LastTargetName { get; private set; }

    public byte[]? Read(string targetName)
    {
      LastTargetName = targetName;
      return Secrets.TryGetValue(targetName, out var secret) ? secret.ToArray() : null;
    }

    public void Write(string targetName, ReadOnlySpan<byte> secret)
    {
      LastTargetName = targetName;
      Secrets[targetName] = secret.ToArray();
    }

    public bool Delete(string targetName)
    {
      LastTargetName = targetName;
      return Secrets.Remove(targetName);
    }
  }
}
