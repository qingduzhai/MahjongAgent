using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Agent.Providers;

public sealed class OpenAiCompatibleProviderProbeRunner : IOpenAiCompatibleProviderProbeRunner
{
  private readonly HttpClient httpClient;
  private readonly IApiKeyCredentialStore credentialStore;

  public OpenAiCompatibleProviderProbeRunner(
    HttpClient httpClient,
    IApiKeyCredentialStore credentialStore)
  {
    ArgumentNullException.ThrowIfNull(httpClient);
    ArgumentNullException.ThrowIfNull(credentialStore);
    this.httpClient = httpClient;
    this.credentialStore = credentialStore;
  }

  public Task<ProviderProbeResult> ProbeAsync(
    OpenAiCompatibleProviderProfile profile,
    string? candidateApiKey,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(profile);
    profile.Validate();

    if (profile.ApiKeyTransport is ApiKeyTransport.None && candidateApiKey is not null)
    {
      throw new ArgumentException(
        "A candidate API key cannot be supplied for a provider that uses no authentication.",
        nameof(candidateApiKey));
    }

    IApiKeyCredentialResolver resolver = candidateApiKey is null
      ? credentialStore
      : new CandidateApiKeyResolver(profile.CredentialId!, candidateApiKey);
    var options = profile.ToOptions();
    var provider = new OpenAiCompatiblePerceptionProvider(httpClient, resolver, options);
    var probe = new OpenAiCompatibleProviderProbe(provider, options);
    return probe.ProbeAsync(cancellationToken);
  }

  private sealed class CandidateApiKeyResolver(
    string expectedCredentialId,
    string candidateApiKey) : IApiKeyCredentialResolver
  {
    public ValueTask<string?> ResolveApiKeyAsync(
      string credentialId,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return ValueTask.FromResult<string?>(
        credentialId.Equals(expectedCredentialId, StringComparison.Ordinal)
          ? candidateApiKey
          : null);
    }
  }
}
