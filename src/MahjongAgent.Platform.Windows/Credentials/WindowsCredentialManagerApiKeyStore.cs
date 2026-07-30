using System.Security.Cryptography;
using System.Text;
using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Platform.Windows.Credentials;

public sealed class WindowsCredentialManagerApiKeyStore : IApiKeyCredentialStore
{
  private const int MaximumCredentialBlobBytes = 2560;
  private const string TargetPrefix = "MahjongAgent:";
  private readonly IWindowsCredentialBackend backend;

  public WindowsCredentialManagerApiKeyStore()
    : this(new WindowsCredentialManagerBackend())
  {
  }

  internal WindowsCredentialManagerApiKeyStore(IWindowsCredentialBackend backend)
  {
    ArgumentNullException.ThrowIfNull(backend);
    this.backend = backend;
  }

  public ValueTask<string?> ResolveApiKeyAsync(
    string credentialId,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var targetName = BuildTargetName(credentialId);
    var secretBytes = backend.Read(targetName);
    if (secretBytes is null)
    {
      return ValueTask.FromResult<string?>(null);
    }

    try
    {
      cancellationToken.ThrowIfCancellationRequested();
      return ValueTask.FromResult<string?>(Encoding.UTF8.GetString(secretBytes));
    }
    finally
    {
      CryptographicOperations.ZeroMemory(secretBytes);
    }
  }

  public ValueTask SaveApiKeyAsync(
    string credentialId,
    string apiKey,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

    if (apiKey.Any(char.IsControl))
    {
      throw new ArgumentException("API keys cannot contain control characters.", nameof(apiKey));
    }

    var targetName = BuildTargetName(credentialId);
    var secretBytes = Encoding.UTF8.GetBytes(apiKey);
    try
    {
      if (secretBytes.Length > MaximumCredentialBlobBytes)
      {
        throw new ArgumentException(
          $"API keys cannot exceed {MaximumCredentialBlobBytes} UTF-8 bytes.",
          nameof(apiKey));
      }

      backend.Write(targetName, secretBytes);
      return ValueTask.CompletedTask;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(secretBytes);
    }
  }

  public ValueTask<bool> DeleteApiKeyAsync(
    string credentialId,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var deleted = backend.Delete(BuildTargetName(credentialId));
    return ValueTask.FromResult(deleted);
  }

  private static string BuildTargetName(string credentialId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);

    if (credentialId.Length > 256 ||
        credentialId.Any(char.IsControl) ||
        !credentialId.Equals(credentialId.Trim(), StringComparison.Ordinal))
    {
      throw new ArgumentException(
        "Credential IDs cannot exceed 256 characters or contain control characters.",
        nameof(credentialId));
    }

    return $"{TargetPrefix}{credentialId}";
  }
}
