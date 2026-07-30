using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MahjongAgent.Perception.Providers;

namespace MahjongAgent.Providers.OpenAICompatible;

public sealed class OpenAiCompatiblePerceptionProvider : IMultimodalPerceptionProvider
{
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
  private readonly HttpClient httpClient;
  private readonly IApiKeyCredentialResolver credentialResolver;
  private readonly OpenAiCompatibleOptions options;
  private readonly Uri chatCompletionsUri;

  public OpenAiCompatiblePerceptionProvider(
    HttpClient httpClient,
    IApiKeyCredentialResolver credentialResolver,
    OpenAiCompatibleOptions options)
  {
    ArgumentNullException.ThrowIfNull(httpClient);
    ArgumentNullException.ThrowIfNull(credentialResolver);
    ArgumentNullException.ThrowIfNull(options);

    options.Validate();

    this.httpClient = httpClient;
    this.credentialResolver = credentialResolver;
    this.options = options;
    chatCompletionsUri = BuildEndpoint(options.BaseUri, options.ChatCompletionsPath);
  }

  public async Task<PerceptionResult> ObserveAsync(
    PerceptionRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    if (request.Images.Count > options.MaxImages)
    {
      throw new ArgumentException(
        $"The request contains more than the configured limit of {options.MaxImages} images.",
        nameof(request));
    }

    long totalImageBytes = 0;
    foreach (var image in request.Images)
    {
      if (image.Data.Length > options.MaxImageBytes)
      {
        throw new ArgumentException(
          $"An image exceeds the configured limit of {options.MaxImageBytes} bytes.",
          nameof(request));
      }

      totalImageBytes += image.Data.Length;
    }

    if (totalImageBytes > options.MaxTotalImageBytes)
    {
      throw new ArgumentException(
        $"The images exceed the configured total limit of {options.MaxTotalImageBytes} bytes.",
        nameof(request));
    }

    var apiKey = await ResolveApiKeyAsync(cancellationToken).ConfigureAwait(false);
    using var message = CreateRequestMessage(request, apiKey);
    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutSource.CancelAfter(options.RequestTimeout);
    var stopwatch = Stopwatch.StartNew();

    HttpResponseMessage response;
    try
    {
      response = await httpClient.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        timeoutSource.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
    {
      throw new OpenAiCompatibleProviderException(
        $"Provider '{options.ProviderId}' timed out after {options.RequestTimeout}.",
        innerException: exception);
    }

    try
    {
      using (response)
      {
        var providerRequestId = GetProviderRequestId(response);

        if (!response.IsSuccessStatusCode)
        {
          var error = await ReadErrorAsync(response, apiKey, timeoutSource.Token).ConfigureAwait(false);
          throw new OpenAiCompatibleProviderException(
            $"Provider '{options.ProviderId}' returned HTTP {(int)response.StatusCode}: {error}",
            response.StatusCode,
            providerRequestId);
        }

        try
        {
          await using var responseStream = await response.Content
            .ReadAsStreamAsync(timeoutSource.Token)
            .ConfigureAwait(false);
          using var document = await JsonDocument
            .ParseAsync(responseStream, cancellationToken: timeoutSource.Token)
            .ConfigureAwait(false);
          stopwatch.Stop();

          return ParseResponse(document.RootElement, providerRequestId, stopwatch.Elapsed);
        }
        catch (JsonException exception)
        {
          throw new OpenAiCompatibleProviderException(
            $"Provider '{options.ProviderId}' returned invalid JSON.",
            response.StatusCode,
            providerRequestId,
            exception);
        }
      }
    }
    catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
    {
      throw new OpenAiCompatibleProviderException(
        $"Provider '{options.ProviderId}' timed out after {options.RequestTimeout}.",
        innerException: exception);
    }
  }

  private async ValueTask<string?> ResolveApiKeyAsync(CancellationToken cancellationToken)
  {
    if (options.ApiKeyTransport is ApiKeyTransport.None)
    {
      return null;
    }

    var apiKey = await credentialResolver
      .ResolveApiKeyAsync(options.CredentialId!, cancellationToken)
      .ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(apiKey) ||
        apiKey.Contains('\r', StringComparison.Ordinal) ||
        apiKey.Contains('\n', StringComparison.Ordinal))
    {
      throw new OpenAiCompatibleProviderException(
        $"Credential '{options.CredentialId}' is missing or empty.");
    }

    return apiKey;
  }

  private HttpRequestMessage CreateRequestMessage(PerceptionRequest request, string? apiKey)
  {
    var message = new HttpRequestMessage(HttpMethod.Post, chatCompletionsUri)
    {
      Content = new StringContent(
        BuildPayload(request).ToJsonString(SerializerOptions),
        Encoding.UTF8,
        "application/json")
    };
    message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    if (!string.IsNullOrWhiteSpace(request.CorrelationId))
    {
      message.Headers.TryAddWithoutValidation("X-Client-Request-Id", request.CorrelationId);
    }

    ApplyAuthentication(message, apiKey);
    foreach (var (name, value) in options.AdditionalHeaders)
    {
      message.Headers.TryAddWithoutValidation(name, value);
    }

    return message;
  }

  private JsonObject BuildPayload(PerceptionRequest request)
  {
    var userPrompt = options.StructuredOutputMode is StructuredOutputMode.JsonObject
      ? BuildJsonObjectPrompt(request)
      : request.UserPrompt;
    var userContent = new JsonArray
    {
      new JsonObject
      {
        ["type"] = "text",
        ["text"] = userPrompt
      }
    };

    foreach (var image in request.Images)
    {
      userContent.Add(new JsonObject
      {
        ["type"] = "image_url",
        ["image_url"] = new JsonObject
        {
          ["url"] = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data.Span)}",
          ["detail"] = options.ImageDetail.ToString().ToLowerInvariant()
        }
      });
    }

    return new JsonObject
    {
      ["model"] = options.PerceptionModel,
      ["messages"] = new JsonArray
      {
        new JsonObject
        {
          ["role"] = "system",
          ["content"] = request.SystemPrompt
        },
        new JsonObject
        {
          ["role"] = "user",
          ["content"] = userContent
        }
      },
      ["response_format"] = BuildResponseFormat(request)
    };
  }

  private JsonObject BuildResponseFormat(PerceptionRequest request) =>
    options.StructuredOutputMode switch
    {
      StructuredOutputMode.JsonSchema => new JsonObject
      {
        ["type"] = "json_schema",
        ["json_schema"] = new JsonObject
        {
          ["name"] = request.OutputSchema.Name,
          ["strict"] = true,
          ["schema"] = JsonNode.Parse(request.OutputSchema.Schema.GetRawText())
        }
      },
      StructuredOutputMode.JsonObject => new JsonObject
      {
        ["type"] = "json_object"
      },
      _ => throw new InvalidOperationException(
        $"Unsupported structured output mode: {options.StructuredOutputMode}.")
    };

  private static string BuildJsonObjectPrompt(PerceptionRequest request) =>
    $"""
    {request.UserPrompt}

    Return one JSON object that matches this schema exactly. Do not include Markdown fences or commentary.
    Schema name: {request.OutputSchema.Name}
    JSON schema:
    {request.OutputSchema.Schema.GetRawText()}
    """;

  private void ApplyAuthentication(HttpRequestMessage message, string? apiKey)
  {
    switch (options.ApiKeyTransport)
    {
      case ApiKeyTransport.None:
        return;
      case ApiKeyTransport.Bearer:
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return;
      case ApiKeyTransport.Header:
        message.Headers.TryAddWithoutValidation(
          options.ApiKeyHeaderName,
          $"{options.ApiKeyPrefix}{apiKey}");
        return;
      default:
        throw new InvalidOperationException($"Unsupported API key transport: {options.ApiKeyTransport}.");
    }
  }

  private PerceptionResult ParseResponse(
    JsonElement root,
    string? providerRequestId,
    TimeSpan duration)
  {
    var content = ExtractMessageContent(root);
    JsonElement structuredOutput;
    try
    {
      using var outputDocument = JsonDocument.Parse(content);
      structuredOutput = outputDocument.RootElement.Clone();
    }
    catch (JsonException exception)
    {
      throw new OpenAiCompatibleProviderException(
        $"Provider '{options.ProviderId}' did not return valid structured output.",
        HttpStatusCode.OK,
        providerRequestId,
        exception);
    }

    if (structuredOutput.ValueKind is not JsonValueKind.Object)
    {
      throw new OpenAiCompatibleProviderException(
        $"Provider '{options.ProviderId}' returned structured output that is not a JSON object.",
        HttpStatusCode.OK,
        providerRequestId);
    }

    var model = TryGetString(root, "model") ?? options.PerceptionModel;
    var usage = ParseUsage(root);

    return new PerceptionResult(
      structuredOutput,
      options.ProviderId,
      model,
      providerRequestId ?? TryGetString(root, "id"),
      usage,
      duration);
  }

  private static string ExtractMessageContent(JsonElement root)
  {
    if (!root.TryGetProperty("choices", out var choices) ||
        choices.ValueKind is not JsonValueKind.Array ||
        choices.GetArrayLength() is 0 ||
        !choices[0].TryGetProperty("message", out var message) ||
        !message.TryGetProperty("content", out var content))
    {
      throw new OpenAiCompatibleProviderException(
        "The provider response does not contain choices[0].message.content.",
        HttpStatusCode.OK);
    }

    if (content.ValueKind is JsonValueKind.String)
    {
      return content.GetString()!;
    }

    if (content.ValueKind is JsonValueKind.Array)
    {
      var builder = new StringBuilder();
      foreach (var part in content.EnumerateArray())
      {
        if (part.TryGetProperty("text", out var text) && text.ValueKind is JsonValueKind.String)
        {
          builder.Append(text.GetString());
        }
      }

      if (builder.Length > 0)
      {
        return builder.ToString();
      }
    }

    throw new OpenAiCompatibleProviderException(
      "The provider response contains no textual structured output.",
      HttpStatusCode.OK);
  }

  private static ModelTokenUsage? ParseUsage(JsonElement root)
  {
    if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind is not JsonValueKind.Object)
    {
      return null;
    }

    return new ModelTokenUsage(
      TryGetInt32(usage, "prompt_tokens"),
      TryGetInt32(usage, "completion_tokens"),
      TryGetInt32(usage, "total_tokens"));
  }

  private static int? TryGetInt32(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
      ? result
      : null;

  private static string? TryGetString(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.String
      ? value.GetString()
      : null;

  private static Uri BuildEndpoint(Uri baseUri, string relativePath)
  {
    var normalizedBaseUri = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
      ? baseUri
      : new Uri($"{baseUri.AbsoluteUri}/", UriKind.Absolute);
    return new Uri(normalizedBaseUri, relativePath);
  }

  private static string? GetProviderRequestId(HttpResponseMessage response)
  {
    foreach (var headerName in new[] { "x-request-id", "request-id", "apim-request-id" })
    {
      if (response.Headers.TryGetValues(headerName, out var values))
      {
        return values.FirstOrDefault();
      }
    }

    return null;
  }

  private static async Task<string> ReadErrorAsync(
    HttpResponseMessage response,
    string? apiKey,
    CancellationToken cancellationToken)
  {
    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    if (!string.IsNullOrEmpty(apiKey))
    {
      body = body.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);
    }

    const int maxErrorLength = 2048;
    return body.Length <= maxErrorLength ? body : $"{body[..maxErrorLength]}…";
  }
}
