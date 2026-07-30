using System.Net;
using System.Text;
using MahjongAgent.Perception.Providers;

namespace MahjongAgent.Providers.OpenAICompatible.Tests;

public sealed class OpenAiCompatibleProviderProbeTests
{
  [Fact]
  public async Task ProbeAsync_verifies_image_and_structured_output_capabilities()
  {
    var provider = new StubPerceptionProvider(new PerceptionResult(
      ParseOutput("{\"left_color\":\"red\",\"right_color\":\"blue\"}"),
      "test-provider",
      "vision-model",
      "request-id",
      null,
      TimeSpan.FromMilliseconds(125)));
    var probe = new OpenAiCompatibleProviderProbe(provider, CreateOptions());

    var result = await probe.ProbeAsync();

    Assert.True(result.IsSuccess);
    Assert.Equal(ProviderProbeFailureKind.None, result.FailureKind);
    Assert.Equal("vision-model", result.Model);
    Assert.Equal("request-id", result.ProviderRequestId);
    Assert.Equal(TimeSpan.FromMilliseconds(125), result.Duration);
    Assert.True(result.VerifiedCapabilities.HasFlag(OpenAiCompatibleCapabilities.Vision));
    Assert.True(result.VerifiedCapabilities.HasFlag(OpenAiCompatibleCapabilities.JsonSchema));
    Assert.NotNull(provider.LastRequest);
    Assert.Single(provider.LastRequest!.Images);
    Assert.Equal("image/png", provider.LastRequest.Images[0].MediaType);
  }

  [Fact]
  public async Task ProbeAsync_reports_capability_mismatch_for_wrong_image_answer()
  {
    var provider = new StubPerceptionProvider(new PerceptionResult(
      ParseOutput("{\"left_color\":\"blue\",\"right_color\":\"red\"}"),
      "test-provider",
      "vision-model",
      null,
      null,
      TimeSpan.Zero));
    var probe = new OpenAiCompatibleProviderProbe(provider, CreateOptions());

    var result = await probe.ProbeAsync();

    Assert.False(result.IsSuccess);
    Assert.Equal(ProviderProbeFailureKind.CapabilityMismatch, result.FailureKind);
    Assert.Equal(OpenAiCompatibleCapabilities.None, result.VerifiedCapabilities);
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized, ProviderProbeFailureKind.Authentication)]
  [InlineData(HttpStatusCode.Forbidden, ProviderProbeFailureKind.Authentication)]
  [InlineData(HttpStatusCode.TooManyRequests, ProviderProbeFailureKind.RateLimited)]
  [InlineData(HttpStatusCode.BadRequest, ProviderProbeFailureKind.Configuration)]
  [InlineData(HttpStatusCode.NotFound, ProviderProbeFailureKind.Configuration)]
  [InlineData(HttpStatusCode.InternalServerError, ProviderProbeFailureKind.ServiceUnavailable)]
  public async Task ProbeAsync_classifies_provider_http_errors(
    HttpStatusCode statusCode,
    ProviderProbeFailureKind expectedFailureKind)
  {
    var provider = new StubPerceptionProvider(new OpenAiCompatibleProviderException(
      "safe error",
      statusCode,
      "request-id"));
    var probe = new OpenAiCompatibleProviderProbe(provider, CreateOptions());

    var result = await probe.ProbeAsync();

    Assert.False(result.IsSuccess);
    Assert.Equal(expectedFailureKind, result.FailureKind);
    Assert.Equal("safe error", result.Message);
    Assert.Equal("request-id", result.ProviderRequestId);
  }

  [Fact]
  public async Task ProbeAsync_preserves_caller_cancellation()
  {
    using var cancellationSource = new CancellationTokenSource();
    cancellationSource.Cancel();
    var provider = new StubPerceptionProvider(new OperationCanceledException(cancellationSource.Token));
    var probe = new OpenAiCompatibleProviderProbe(provider, CreateOptions());

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => probe.ProbeAsync(cancellationSource.Token));
  }

  private static OpenAiCompatibleOptions CreateOptions() => new()
  {
    ProviderId = "test-provider",
    BaseUri = new Uri("https://provider.example/v1/"),
    PerceptionModel = "vision-model",
    CredentialId = "credential-id"
  };

  private static System.Text.Json.JsonElement ParseOutput(string json)
  {
    using var document = System.Text.Json.JsonDocument.Parse(json);
    return document.RootElement.Clone();
  }

  private sealed class StubPerceptionProvider : IMultimodalPerceptionProvider
  {
    private readonly Exception? exception;
    private readonly PerceptionResult? result;

    public StubPerceptionProvider(PerceptionResult result)
    {
      this.result = result;
    }

    public StubPerceptionProvider(Exception exception)
    {
      this.exception = exception;
    }

    public PerceptionRequest? LastRequest { get; private set; }

    public Task<PerceptionResult> ObserveAsync(
      PerceptionRequest request,
      CancellationToken cancellationToken = default)
    {
      LastRequest = request;

      if (exception is not null)
      {
        return Task.FromException<PerceptionResult>(exception);
      }

      return Task.FromResult(result!);
    }
  }
}
