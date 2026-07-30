namespace MahjongAgent.Providers.OpenAICompatible;

public sealed record ProviderProbeSnapshot
{
  public required DateTimeOffset ProbedAtUtc { get; init; }

  public required bool IsSuccess { get; init; }

  public required ProviderProbeFailureKind FailureKind { get; init; }

  public OpenAiCompatibleCapabilities VerifiedCapabilities { get; init; }

  public string? ActualModel { get; init; }

  public long? DurationMilliseconds { get; init; }

  public string? ProviderRequestId { get; init; }

  public static ProviderProbeSnapshot FromResult(
    ProviderProbeResult result,
    DateTimeOffset probedAtUtc)
  {
    ArgumentNullException.ThrowIfNull(result);

    return new ProviderProbeSnapshot
    {
      ProbedAtUtc = probedAtUtc.ToUniversalTime(),
      IsSuccess = result.IsSuccess,
      FailureKind = result.FailureKind,
      VerifiedCapabilities = result.VerifiedCapabilities,
      ActualModel = result.Model,
      DurationMilliseconds = result.IsSuccess
        ? checked((long)Math.Ceiling(result.Duration.TotalMilliseconds))
        : null,
      ProviderRequestId = result.ProviderRequestId
    };
  }

  internal void Validate()
  {
    if (ProbedAtUtc.Offset != TimeSpan.Zero)
    {
      throw new ArgumentException("Probe timestamps must use UTC.", nameof(ProbedAtUtc));
    }

    if (DurationMilliseconds is < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(DurationMilliseconds));
    }

    if (IsSuccess)
    {
      if (FailureKind is not ProviderProbeFailureKind.None)
      {
        throw new ArgumentException("A successful probe cannot have a failure kind.", nameof(FailureKind));
      }

      ArgumentException.ThrowIfNullOrWhiteSpace(ActualModel);
      if (!VerifiedCapabilities.HasFlag(OpenAiCompatibleCapabilities.Vision))
      {
        throw new ArgumentException(
          "A successful perception probe must verify vision support.",
          nameof(VerifiedCapabilities));
      }
    }
    else if (FailureKind is ProviderProbeFailureKind.None)
    {
      throw new ArgumentException("A failed probe must have a failure kind.", nameof(FailureKind));
    }
  }
}
