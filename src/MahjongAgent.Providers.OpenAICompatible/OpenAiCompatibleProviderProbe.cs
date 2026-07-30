using System.Net;
using System.Text.Json;
using MahjongAgent.Perception.Providers;

namespace MahjongAgent.Providers.OpenAICompatible;

public sealed class OpenAiCompatibleProviderProbe
{
  private const string SyntheticImageBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAEAAAAAgCAYAAACinX6EAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAACMSURBVGhDxcghAcBAEASx82/6K2AHpyAk9+7en6KsHSvK2rGirB0rytqxoqwdK8rasaKsHSvK2rGirB0rytqxoqwdK8rasaKsHSvK2rGirB0rytqxoqwdK8rasaKsHSvK2rGirB0rytqxoqwdK8rasaKsHSvK2rGirB0rytqxoqwdK8rasaKsHSuK+gAoevDiv7fCPAAAAABJRU5ErkJggg==";

  private readonly IMultimodalPerceptionProvider provider;
  private readonly OpenAiCompatibleOptions options;

  public OpenAiCompatibleProviderProbe(
    IMultimodalPerceptionProvider provider,
    OpenAiCompatibleOptions options)
  {
    ArgumentNullException.ThrowIfNull(provider);
    ArgumentNullException.ThrowIfNull(options);

    options.Validate();
    this.provider = provider;
    this.options = options;
  }

  public async Task<ProviderProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var result = await provider
        .ObserveAsync(CreateProbeRequest(), cancellationToken)
        .ConfigureAwait(false);

      if (!MatchesSyntheticImage(result.StructuredOutput))
      {
        return ProviderProbeResult.Failure(
          options.ProviderId,
          ProviderProbeFailureKind.CapabilityMismatch,
          "The endpoint returned structured JSON but did not correctly identify the synthetic test image.",
          result.ProviderRequestId);
      }

      var structuredCapability = options.StructuredOutputMode switch
      {
        StructuredOutputMode.JsonSchema => OpenAiCompatibleCapabilities.JsonSchema,
        StructuredOutputMode.JsonObject => OpenAiCompatibleCapabilities.JsonObject,
        _ => OpenAiCompatibleCapabilities.None
      };

      return ProviderProbeResult.Success(
        result.ProviderId,
        result.Model,
        result.ProviderRequestId,
        result.Duration,
        OpenAiCompatibleCapabilities.Vision | structuredCapability);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (OpenAiCompatibleProviderException exception)
    {
      return ProviderProbeResult.Failure(
        options.ProviderId,
        ClassifyFailure(exception),
        exception.Message,
        exception.ProviderRequestId);
    }
    catch (Exception exception)
    {
      return ProviderProbeResult.Failure(
        options.ProviderId,
        ProviderProbeFailureKind.Unknown,
        $"Provider probe failed: {exception.GetType().Name}.");
    }
  }

  private static PerceptionRequest CreateProbeRequest()
  {
    using var schemaDocument = JsonDocument.Parse(
      """
      {
        "type": "object",
        "properties": {
          "left_color": {
            "type": "string",
            "enum": ["red"]
          },
          "right_color": {
            "type": "string",
            "enum": ["blue"]
          }
        },
        "required": ["left_color", "right_color"],
        "additionalProperties": false
      }
      """);

    return new PerceptionRequest(
      "You are validating a multimodal API connection. Follow the output schema exactly.",
      "Identify the solid color on the left half and the solid color on the right half of this synthetic image.",
      [new PerceptionImage("image/png", Convert.FromBase64String(SyntheticImageBase64))],
      new StructuredOutputSchema("provider_capability_probe", schemaDocument.RootElement),
      $"provider-probe-{Guid.NewGuid():N}");
  }

  private static bool MatchesSyntheticImage(JsonElement output) =>
    output.TryGetProperty("left_color", out var leftColor) &&
    leftColor.ValueKind is JsonValueKind.String &&
    leftColor.GetString()?.Equals("red", StringComparison.OrdinalIgnoreCase) is true &&
    output.TryGetProperty("right_color", out var rightColor) &&
    rightColor.ValueKind is JsonValueKind.String &&
    rightColor.GetString()?.Equals("blue", StringComparison.OrdinalIgnoreCase) is true;

  private static ProviderProbeFailureKind ClassifyFailure(
    OpenAiCompatibleProviderException exception) =>
    exception.StatusCode switch
    {
      HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
        ProviderProbeFailureKind.Authentication,
      HttpStatusCode.TooManyRequests => ProviderProbeFailureKind.RateLimited,
      HttpStatusCode.BadRequest or HttpStatusCode.NotFound =>
        ProviderProbeFailureKind.Configuration,
      >= HttpStatusCode.InternalServerError => ProviderProbeFailureKind.ServiceUnavailable,
      HttpStatusCode.OK => ProviderProbeFailureKind.Protocol,
      null when exception.InnerException is OperationCanceledException =>
        ProviderProbeFailureKind.Timeout,
      _ => ProviderProbeFailureKind.Unknown
    };
}
