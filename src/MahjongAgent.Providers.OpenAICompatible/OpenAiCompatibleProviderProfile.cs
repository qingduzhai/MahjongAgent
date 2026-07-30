using System.Text.RegularExpressions;

namespace MahjongAgent.Providers.OpenAICompatible;

public sealed partial record OpenAiCompatibleProviderProfile
{
  public required string Id { get; init; }

  public required string DisplayName { get; init; }

  public required Uri BaseUri { get; init; }

  public bool AllowInsecureHttp { get; init; }

  public string ChatCompletionsPath { get; init; } = "chat/completions";

  public required string PerceptionModel { get; init; }

  public string? CredentialId { get; init; }

  public ApiKeyTransport ApiKeyTransport { get; init; } = ApiKeyTransport.Bearer;

  public string ApiKeyHeaderName { get; init; } = "api-key";

  public string ApiKeyPrefix { get; init; } = string.Empty;

  public OpenAiCompatibleCapabilities Capabilities { get; init; } =
    OpenAiCompatibleCapabilities.Vision |
    OpenAiCompatibleCapabilities.JsonSchema |
    OpenAiCompatibleCapabilities.JsonObject;

  public StructuredOutputMode StructuredOutputMode { get; init; } = StructuredOutputMode.JsonSchema;

  public ImageDetail ImageDetail { get; init; } = ImageDetail.High;

  public IReadOnlyDictionary<string, string> AdditionalHeaders { get; init; } =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

  public ProviderProbeSnapshot? LastProbe { get; init; }

  public OpenAiCompatibleOptions ToOptions()
  {
    Validate();

    return new OpenAiCompatibleOptions
    {
      ProviderId = Id,
      BaseUri = BaseUri,
      AllowInsecureHttp = AllowInsecureHttp,
      ChatCompletionsPath = ChatCompletionsPath,
      PerceptionModel = PerceptionModel,
      CredentialId = CredentialId,
      ApiKeyTransport = ApiKeyTransport,
      ApiKeyHeaderName = ApiKeyHeaderName,
      ApiKeyPrefix = ApiKeyPrefix,
      Capabilities = Capabilities,
      StructuredOutputMode = StructuredOutputMode,
      ImageDetail = ImageDetail,
      AdditionalHeaders = new Dictionary<string, string>(
        AdditionalHeaders,
        StringComparer.OrdinalIgnoreCase)
    };
  }

  public void Validate()
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(Id);
    ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

    if (!ProfileIdPattern().IsMatch(Id))
    {
      throw new ArgumentException(
        "Profile IDs may contain only letters, numbers, dots, underscores and hyphens, with a maximum length of 64.",
        nameof(Id));
    }

    if (DisplayName.Length > 128)
    {
      throw new ArgumentException("Display names cannot exceed 128 characters.", nameof(DisplayName));
    }

    new OpenAiCompatibleOptions
    {
      ProviderId = Id,
      BaseUri = BaseUri,
      AllowInsecureHttp = AllowInsecureHttp,
      ChatCompletionsPath = ChatCompletionsPath,
      PerceptionModel = PerceptionModel,
      CredentialId = CredentialId,
      ApiKeyTransport = ApiKeyTransport,
      ApiKeyHeaderName = ApiKeyHeaderName,
      ApiKeyPrefix = ApiKeyPrefix,
      Capabilities = Capabilities,
      StructuredOutputMode = StructuredOutputMode,
      ImageDetail = ImageDetail,
      AdditionalHeaders = AdditionalHeaders
    }.Validate();

    LastProbe?.Validate();
  }

  [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
  private static partial Regex ProfileIdPattern();
}
