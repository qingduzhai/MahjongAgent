using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Agent.Providers;

public interface IOpenAiCompatibleProviderProbeRunner
{
  Task<ProviderProbeResult> ProbeAsync(
    OpenAiCompatibleProviderProfile profile,
    string? candidateApiKey,
    CancellationToken cancellationToken = default);
}
