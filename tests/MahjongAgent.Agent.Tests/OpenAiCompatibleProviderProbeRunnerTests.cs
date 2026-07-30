using System.Net;
using System.Text;
using MahjongAgent.Agent.Providers;
using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Agent.Tests;

public sealed class OpenAiCompatibleProviderProbeRunnerTests
{
  [Fact]
  public async Task ProbeAsync_uses_candidate_key_without_persisting_it()
  {
    var credentialStore = new RecordingCredentialStore("stored-key");
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      Assert.Equal("candidate-key", request.Headers.Authorization?.Parameter);
      return Task.FromResult(SuccessResponse());
    });
    var runner = new OpenAiCompatibleProviderProbeRunner(
      new HttpClient(handler),
      credentialStore);

    var result = await runner.ProbeAsync(CreateProfile(), "candidate-key");

    Assert.True(result.IsSuccess);
    Assert.Equal(0, credentialStore.ResolveCount);
    Assert.Equal(0, credentialStore.SaveCount);
  }

  [Fact]
  public async Task ProbeAsync_uses_saved_key_when_no_candidate_is_supplied()
  {
    var credentialStore = new RecordingCredentialStore("stored-key");
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      Assert.Equal("stored-key", request.Headers.Authorization?.Parameter);
      return Task.FromResult(SuccessResponse());
    });
    var runner = new OpenAiCompatibleProviderProbeRunner(
      new HttpClient(handler),
      credentialStore);

    var result = await runner.ProbeAsync(CreateProfile(), null);

    Assert.True(result.IsSuccess);
    Assert.Equal(1, credentialStore.ResolveCount);
    Assert.Equal(0, credentialStore.SaveCount);
  }

  [Fact]
  public async Task ProbeAsync_rejects_candidate_key_for_no_auth_profile()
  {
    var runner = new OpenAiCompatibleProviderProbeRunner(
      new HttpClient(new StubHttpMessageHandler()),
      new RecordingCredentialStore(null));
    var profile = CreateProfile() with
    {
      BaseUri = new Uri("http://localhost:1234/v1/"),
      ApiKeyTransport = ApiKeyTransport.None,
      CredentialId = null
    };

    await Assert.ThrowsAsync<ArgumentException>(
      () => runner.ProbeAsync(profile, "unexpected-key"));
  }

  private static OpenAiCompatibleProviderProfile CreateProfile() => new()
  {
    Id = "provider-test",
    DisplayName = "Provider Test",
    BaseUri = new Uri("https://provider.example/v1/"),
    PerceptionModel = "vision-model",
    CredentialId = "mahjong-agent/provider/test",
    ApiKeyTransport = ApiKeyTransport.Bearer,
    Capabilities =
      OpenAiCompatibleCapabilities.Vision |
      OpenAiCompatibleCapabilities.JsonSchema,
    StructuredOutputMode = StructuredOutputMode.JsonSchema
  };

  private static HttpResponseMessage SuccessResponse() => new(HttpStatusCode.OK)
  {
    Content = new StringContent(
      """
      {
        "model": "vision-model-actual",
        "choices": [
          {
            "message": {
              "content": "{\"left_color\":\"red\",\"right_color\":\"blue\"}"
            }
          }
        ]
      }
      """,
      Encoding.UTF8,
      "application/json")
  };

  private sealed class RecordingCredentialStore(string? storedKey) : IApiKeyCredentialStore
  {
    public int ResolveCount { get; private set; }

    public int SaveCount { get; private set; }

    public ValueTask<string?> ResolveApiKeyAsync(
      string credentialId,
      CancellationToken cancellationToken = default)
    {
      ResolveCount++;
      return ValueTask.FromResult(storedKey);
    }

    public ValueTask SaveApiKeyAsync(
      string credentialId,
      string apiKey,
      CancellationToken cancellationToken = default)
    {
      SaveCount++;
      return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteApiKeyAsync(
      string credentialId,
      CancellationToken cancellationToken = default) =>
      ValueTask.FromResult(false);
  }

  private sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? callback = null)
    : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken) =>
      callback?.Invoke(request, cancellationToken) ?? Task.FromResult(SuccessResponse());
  }
}
