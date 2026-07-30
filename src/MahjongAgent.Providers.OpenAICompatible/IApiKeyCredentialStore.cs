namespace MahjongAgent.Providers.OpenAICompatible;

public interface IApiKeyCredentialStore : IApiKeyCredentialResolver
{
  ValueTask SaveApiKeyAsync(
    string credentialId,
    string apiKey,
    CancellationToken cancellationToken = default);

  ValueTask<bool> DeleteApiKeyAsync(
    string credentialId,
    CancellationToken cancellationToken = default);
}
