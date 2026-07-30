using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Agent.Providers;

public sealed record ProviderProfileCommitResult(
  bool IsSaved,
  OpenAiCompatibleProviderProfile Profile,
  ProviderProbeResult ProbeResult);
