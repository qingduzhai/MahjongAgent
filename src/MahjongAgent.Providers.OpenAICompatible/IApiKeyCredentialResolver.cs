namespace MahjongAgent.Providers.OpenAICompatible;

public interface IApiKeyCredentialResolver
{
  ValueTask<string?> ResolveApiKeyAsync(
    string credentialId,
    CancellationToken cancellationToken = default);
}
