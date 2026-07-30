namespace MahjongAgent.Agent.Providers;

public sealed class ProviderProfileTransactionException : Exception
{
  public ProviderProfileTransactionException(
    string message,
    Exception operationException,
    IReadOnlyList<Exception> compensationExceptions)
    : base(message, operationException)
  {
    CompensationExceptions = compensationExceptions;
  }

  public IReadOnlyList<Exception> CompensationExceptions { get; }
}
