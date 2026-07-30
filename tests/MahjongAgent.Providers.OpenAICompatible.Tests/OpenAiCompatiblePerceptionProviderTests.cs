using System.Net;
using System.Text;
using System.Text.Json;
using MahjongAgent.Perception.Providers;

namespace MahjongAgent.Providers.OpenAICompatible.Tests;

public sealed class OpenAiCompatiblePerceptionProviderTests
{
  [Fact]
  public async Task ObserveAsync_sends_multimodal_json_schema_request_and_parses_result()
  {
    var handler = new RecordingHandler(async (request, cancellationToken) =>
    {
      Assert.Equal(HttpMethod.Post, request.Method);
      Assert.Equal("https://provider.example/v1/chat/completions", request.RequestUri?.AbsoluteUri);
      Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
      Assert.Equal("secret-key", request.Headers.Authorization?.Parameter);
      Assert.Equal("game-42", request.Headers.GetValues("X-Client-Request-Id").Single());

      var body = await request.Content!.ReadAsStringAsync(cancellationToken);
      using var document = JsonDocument.Parse(body);
      var root = document.RootElement;
      Assert.Equal("vision-model", root.GetProperty("model").GetString());
      Assert.Equal("system instructions", root.GetProperty("messages")[0].GetProperty("content").GetString());

      var userContent = root.GetProperty("messages")[1].GetProperty("content");
      Assert.Equal("observe table", userContent[0].GetProperty("text").GetString());
      Assert.Equal("image_url", userContent[1].GetProperty("type").GetString());
      Assert.Equal(
        "data:image/png;base64,AQID",
        userContent[1].GetProperty("image_url").GetProperty("url").GetString());
      Assert.Equal(
        "high",
        userContent[1].GetProperty("image_url").GetProperty("detail").GetString());

      var responseFormat = root.GetProperty("response_format");
      Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
      Assert.True(responseFormat.GetProperty("json_schema").GetProperty("strict").GetBoolean());
      Assert.Equal(
        "observation_batch",
        responseFormat.GetProperty("json_schema").GetProperty("name").GetString());

      var response = JsonResponse(
        """
        {
          "id": "body-request-id",
          "model": "vision-model-2026-07",
          "choices": [
            {
              "message": {
                "content": "{\"events\":[]}"
              }
            }
          ],
          "usage": {
            "prompt_tokens": 120,
            "completion_tokens": 8,
            "total_tokens": 128
          }
        }
        """);
      response.Headers.Add("x-request-id", "header-request-id");
      return response;
    });
    var provider = CreateProvider(handler);

    var result = await provider.ObserveAsync(CreateRequest("game-42"));

    Assert.Equal("openai-compatible-test", result.ProviderId);
    Assert.Equal("vision-model-2026-07", result.Model);
    Assert.Equal("header-request-id", result.ProviderRequestId);
    Assert.Equal(JsonValueKind.Array, result.StructuredOutput.GetProperty("events").ValueKind);
    Assert.Equal(120, result.TokenUsage?.InputTokens);
    Assert.Equal(8, result.TokenUsage?.OutputTokens);
    Assert.Equal(128, result.TokenUsage?.TotalTokens);
  }

  [Fact]
  public async Task ObserveAsync_supports_custom_api_key_header()
  {
    var handler = new RecordingHandler((request, _) =>
    {
      Assert.Null(request.Headers.Authorization);
      Assert.Equal("Token secret-key", request.Headers.GetValues("X-Api-Key").Single());
      return Task.FromResult(JsonResponse(SuccessResponse));
    });
    var options = CreateOptions() with
    {
      ApiKeyTransport = ApiKeyTransport.Header,
      ApiKeyHeaderName = "X-Api-Key",
      ApiKeyPrefix = "Token "
    };
    var provider = CreateProvider(handler, options);

    await provider.ObserveAsync(CreateRequest());
  }

  [Fact]
  public async Task ObserveAsync_supports_json_object_fallback()
  {
    var handler = new RecordingHandler(async (request, cancellationToken) =>
    {
      var body = await request.Content!.ReadAsStringAsync(cancellationToken);
      using var document = JsonDocument.Parse(body);
      var root = document.RootElement;

      Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
      var prompt = root.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString();
      Assert.Contains("JSON schema", prompt, StringComparison.Ordinal);
      Assert.Contains("additionalProperties", prompt, StringComparison.Ordinal);
      return JsonResponse(SuccessResponse);
    });
    var options = CreateOptions() with
    {
      Capabilities =
        OpenAiCompatibleCapabilities.Vision | OpenAiCompatibleCapabilities.JsonObject,
      StructuredOutputMode = StructuredOutputMode.JsonObject
    };
    var provider = CreateProvider(handler, options);

    await provider.ObserveAsync(CreateRequest());
  }

  [Fact]
  public async Task ObserveAsync_does_not_require_a_key_for_local_provider()
  {
    var resolver = new StubCredentialResolver("must-not-be-read");
    var handler = new RecordingHandler((request, _) =>
    {
      Assert.Null(request.Headers.Authorization);
      return Task.FromResult(JsonResponse(SuccessResponse));
    });
    var options = CreateOptions() with
    {
      ApiKeyTransport = ApiKeyTransport.None,
      CredentialId = null
    };
    var provider = new OpenAiCompatiblePerceptionProvider(
      new HttpClient(handler),
      resolver,
      options);

    await provider.ObserveAsync(CreateRequest());

    Assert.Equal(0, resolver.ResolveCount);
  }

  [Fact]
  public async Task ObserveAsync_redacts_api_key_from_error_response()
  {
    var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
    {
      Content = new StringContent("upstream echoed secret-key", Encoding.UTF8, "text/plain")
    }));
    var provider = CreateProvider(handler);

    var exception = await Assert.ThrowsAsync<OpenAiCompatibleProviderException>(
      () => provider.ObserveAsync(CreateRequest()));

    Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    Assert.DoesNotContain("secret-key", exception.Message, StringComparison.Ordinal);
    Assert.Contains("[REDACTED]", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ObserveAsync_rejects_non_json_structured_output()
  {
    var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
      """
      {
        "choices": [
          {
            "message": {
              "content": "not-json"
            }
          }
        ]
      }
      """)));
    var provider = CreateProvider(handler);

    var exception = await Assert.ThrowsAsync<OpenAiCompatibleProviderException>(
      () => provider.ObserveAsync(CreateRequest()));

    Assert.Contains("structured output", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task ObserveAsync_rejects_structured_output_that_is_not_an_object()
  {
    var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
      """
      {
        "choices": [
          {
            "message": {
              "content": "[]"
            }
          }
        ]
      }
      """)));
    var provider = CreateProvider(handler);

    var exception = await Assert.ThrowsAsync<OpenAiCompatibleProviderException>(
      () => provider.ObserveAsync(CreateRequest()));

    Assert.Contains("not a JSON object", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Constructor_rejects_provider_without_selected_structured_output_capability()
  {
    var options = CreateOptions() with
    {
      Capabilities = OpenAiCompatibleCapabilities.Vision
    };

    Assert.Throws<ArgumentException>(() => CreateProvider(new RecordingHandler(), options));
  }

  [Fact]
  public void Constructor_rejects_authentication_header_in_static_configuration()
  {
    var options = CreateOptions() with
    {
      AdditionalHeaders = new Dictionary<string, string>
      {
        ["Authorization"] = "Bearer should-not-be-here"
      }
    };

    Assert.Throws<ArgumentException>(() => CreateProvider(new RecordingHandler(), options));
  }

  [Fact]
  public void Constructor_rejects_insecure_remote_endpoint_by_default()
  {
    var options = CreateOptions() with
    {
      BaseUri = new Uri("http://provider.example/v1/")
    };

    Assert.Throws<ArgumentException>(() => CreateProvider(new RecordingHandler(), options));
  }

  [Fact]
  public void Perception_request_rejects_header_injection_in_correlation_id()
  {
    Assert.Throws<ArgumentException>(() => CreateRequest("valid\r\ninjected: true"));
  }

  private static OpenAiCompatiblePerceptionProvider CreateProvider(
    HttpMessageHandler handler,
    OpenAiCompatibleOptions? options = null) =>
    new(
      new HttpClient(handler),
      new StubCredentialResolver("secret-key"),
      options ?? CreateOptions());

  private static OpenAiCompatibleOptions CreateOptions() => new()
  {
    ProviderId = "openai-compatible-test",
    BaseUri = new Uri("https://provider.example/v1/"),
    PerceptionModel = "vision-model",
    CredentialId = "credential-id"
  };

  private static PerceptionRequest CreateRequest(string? correlationId = null)
  {
    using var schemaDocument = JsonDocument.Parse(
      """
      {
        "type": "object",
        "properties": {
          "events": {
            "type": "array"
          }
        },
        "required": ["events"],
        "additionalProperties": false
      }
      """);

    return new PerceptionRequest(
      "system instructions",
      "observe table",
      [new PerceptionImage("image/png", new byte[] { 1, 2, 3 })],
      new StructuredOutputSchema("observation_batch", schemaDocument.RootElement),
      correlationId);
  }

  private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(json, Encoding.UTF8, "application/json")
  };

  private const string SuccessResponse =
    """
    {
      "model": "vision-model",
      "choices": [
        {
          "message": {
            "content": "{\"events\":[]}"
          }
        }
      ]
    }
    """;

  private sealed class StubCredentialResolver(string? apiKey) : IApiKeyCredentialResolver
  {
    public int ResolveCount { get; private set; }

    public ValueTask<string?> ResolveApiKeyAsync(
      string credentialId,
      CancellationToken cancellationToken = default)
    {
      ResolveCount++;
      return ValueTask.FromResult(apiKey);
    }
  }

  private sealed class RecordingHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? callback = null)
    : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken) =>
      callback?.Invoke(request, cancellationToken) ?? Task.FromResult(JsonResponse(SuccessResponse));
  }
}
