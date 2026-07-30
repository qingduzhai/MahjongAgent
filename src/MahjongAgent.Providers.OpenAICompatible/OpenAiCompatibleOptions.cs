namespace MahjongAgent.Providers.OpenAICompatible;

public sealed record OpenAiCompatibleOptions
{
  public required string ProviderId { get; init; }

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

  public int MaxImageBytes { get; init; } = 10 * 1024 * 1024;

  public int MaxImages { get; init; } = 4;

  public int MaxTotalImageBytes { get; init; } = 20 * 1024 * 1024;

  public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

  public IReadOnlyDictionary<string, string> AdditionalHeaders { get; init; } =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

  internal void Validate()
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
    ArgumentException.ThrowIfNullOrWhiteSpace(PerceptionModel);
    ArgumentNullException.ThrowIfNull(BaseUri);

    if (!BaseUri.IsAbsoluteUri ||
        (BaseUri.Scheme is not "http" && BaseUri.Scheme is not "https"))
    {
      throw new ArgumentException("BaseUri must be an absolute HTTP or HTTPS URI.", nameof(BaseUri));
    }

    if (BaseUri.Scheme is "http" && !BaseUri.IsLoopback && !AllowInsecureHttp)
    {
      throw new ArgumentException(
        "Plain HTTP is allowed only for loopback endpoints unless AllowInsecureHttp is explicitly enabled.",
        nameof(BaseUri));
    }

    if (!string.IsNullOrEmpty(BaseUri.UserInfo) ||
        !string.IsNullOrEmpty(BaseUri.Query) ||
        !string.IsNullOrEmpty(BaseUri.Fragment))
    {
      throw new ArgumentException(
        "BaseUri cannot contain credentials, a query string or a fragment.",
        nameof(BaseUri));
    }

    if (string.IsNullOrWhiteSpace(ChatCompletionsPath) ||
        Uri.TryCreate(ChatCompletionsPath, UriKind.Absolute, out _) ||
        ChatCompletionsPath.StartsWith('/') ||
        ChatCompletionsPath.Contains("..", StringComparison.Ordinal))
    {
      throw new ArgumentException(
        "ChatCompletionsPath must be a safe path relative to BaseUri.",
        nameof(ChatCompletionsPath));
    }

    if (MaxImageBytes <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(MaxImageBytes));
    }

    if (MaxImages <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(MaxImages));
    }

    if (MaxTotalImageBytes < MaxImageBytes)
    {
      throw new ArgumentOutOfRangeException(
        nameof(MaxTotalImageBytes),
        "The total image limit cannot be smaller than the per-image limit.");
    }

    if (RequestTimeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
    }

    if (ApiKeyTransport is not ApiKeyTransport.None)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(CredentialId);
    }

    if (ApiKeyTransport is ApiKeyTransport.Header)
    {
      ValidateHeader(ApiKeyHeaderName, ApiKeyPrefix, nameof(ApiKeyHeaderName));
    }

    foreach (var (name, value) in AdditionalHeaders)
    {
      ValidateHeader(name, value, nameof(AdditionalHeaders));

      if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
          (ApiKeyTransport is ApiKeyTransport.Header &&
           name.Equals(ApiKeyHeaderName, StringComparison.OrdinalIgnoreCase)))
      {
        throw new ArgumentException(
          "Authentication headers must be supplied through the credential resolver.",
          nameof(AdditionalHeaders));
      }
    }

    if (!Capabilities.HasFlag(OpenAiCompatibleCapabilities.Vision))
    {
      throw new ArgumentException(
        "The perception provider requires the Vision capability.",
        nameof(Capabilities));
    }

    var structuredOutputCapability = StructuredOutputMode switch
    {
      StructuredOutputMode.JsonSchema => OpenAiCompatibleCapabilities.JsonSchema,
      StructuredOutputMode.JsonObject => OpenAiCompatibleCapabilities.JsonObject,
      _ => throw new ArgumentOutOfRangeException(nameof(StructuredOutputMode))
    };

    if (!Capabilities.HasFlag(structuredOutputCapability))
    {
      throw new ArgumentException(
        $"The selected structured output mode requires the {structuredOutputCapability} capability.",
        nameof(Capabilities));
    }
  }

  private static void ValidateHeader(string name, string value, string parameterName)
  {
    if (string.IsNullOrWhiteSpace(name) ||
        name.Contains('\r', StringComparison.Ordinal) ||
        name.Contains('\n', StringComparison.Ordinal) ||
        value.Contains('\r', StringComparison.Ordinal) ||
        value.Contains('\n', StringComparison.Ordinal))
    {
      throw new ArgumentException("HTTP header names and values cannot be empty or contain newlines.", parameterName);
    }
  }
}
