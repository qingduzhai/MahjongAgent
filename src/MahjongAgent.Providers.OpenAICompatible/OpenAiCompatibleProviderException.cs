using System.Net;

namespace MahjongAgent.Providers.OpenAICompatible;

public sealed class OpenAiCompatibleProviderException : Exception
{
  public OpenAiCompatibleProviderException(
    string message,
    HttpStatusCode? statusCode = null,
    string? providerRequestId = null,
    Exception? innerException = null)
    : base(message, innerException)
  {
    StatusCode = statusCode;
    ProviderRequestId = providerRequestId;
  }

  public HttpStatusCode? StatusCode { get; }

  public string? ProviderRequestId { get; }
}
