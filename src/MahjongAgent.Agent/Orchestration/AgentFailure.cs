namespace MahjongAgent.Agent.Orchestration;

public sealed record AgentFailure(string Code, string Message, bool IsRecoverable)
{
  public AgentFailure Validate()
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(Code);
    ArgumentException.ThrowIfNullOrWhiteSpace(Message);
    return this;
  }
}
