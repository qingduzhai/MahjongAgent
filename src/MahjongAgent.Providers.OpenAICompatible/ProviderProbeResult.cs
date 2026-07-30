namespace MahjongAgent.Providers.OpenAICompatible;

public sealed record ProviderProbeResult
{
  private ProviderProbeResult(
    bool isSuccess,
    ProviderProbeFailureKind failureKind,
    string message,
    string providerId,
    string? model,
    string? providerRequestId,
    TimeSpan duration,
    OpenAiCompatibleCapabilities verifiedCapabilities)
  {
    IsSuccess = isSuccess;
    FailureKind = failureKind;
    Message = message;
    ProviderId = providerId;
    Model = model;
    ProviderRequestId = providerRequestId;
    Duration = duration;
    VerifiedCapabilities = verifiedCapabilities;
  }

  public bool IsSuccess { get; }

  public ProviderProbeFailureKind FailureKind { get; }

  public string Message { get; }

  public string ProviderId { get; }

  public string? Model { get; }

  public string? ProviderRequestId { get; }

  public TimeSpan Duration { get; }

  public OpenAiCompatibleCapabilities VerifiedCapabilities { get; }

  public static ProviderProbeResult Success(
    string providerId,
    string model,
    string? providerRequestId,
    TimeSpan duration,
    OpenAiCompatibleCapabilities verifiedCapabilities) =>
    new(
      true,
      ProviderProbeFailureKind.None,
      "Connection, image input and structured output are available.",
      providerId,
      model,
      providerRequestId,
      duration,
      verifiedCapabilities);

  public static ProviderProbeResult Failure(
    string providerId,
    ProviderProbeFailureKind failureKind,
    string message,
    string? providerRequestId = null) =>
    new(
      false,
      failureKind,
      message,
      providerId,
      null,
      providerRequestId,
      TimeSpan.Zero,
      OpenAiCompatibleCapabilities.None);
}
