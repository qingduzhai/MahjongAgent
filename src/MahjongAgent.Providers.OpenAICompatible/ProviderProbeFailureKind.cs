namespace MahjongAgent.Providers.OpenAICompatible;

public enum ProviderProbeFailureKind
{
  None = 0,
  Configuration = 1,
  Authentication = 2,
  RateLimited = 3,
  ServiceUnavailable = 4,
  Timeout = 5,
  Protocol = 6,
  CapabilityMismatch = 7,
  Unknown = 8
}
