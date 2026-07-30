using System.Text.Json;

namespace MahjongAgent.Perception.Providers;

public sealed record PerceptionResult
{
  public PerceptionResult(
    JsonElement structuredOutput,
    string providerId,
    string model,
    string? providerRequestId,
    ModelTokenUsage? tokenUsage,
    TimeSpan duration)
  {
    if (structuredOutput.ValueKind is not JsonValueKind.Object)
    {
      throw new ArgumentException("Perception output must be a JSON object.", nameof(structuredOutput));
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
    ArgumentException.ThrowIfNullOrWhiteSpace(model);

    StructuredOutput = structuredOutput.Clone();
    ProviderId = providerId;
    Model = model;
    ProviderRequestId = providerRequestId;
    TokenUsage = tokenUsage;
    Duration = duration;
  }

  public JsonElement StructuredOutput { get; }

  public string ProviderId { get; }

  public string Model { get; }

  public string? ProviderRequestId { get; }

  public ModelTokenUsage? TokenUsage { get; }

  public TimeSpan Duration { get; }
}
