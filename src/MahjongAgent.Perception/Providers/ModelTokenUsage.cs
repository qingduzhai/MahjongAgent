namespace MahjongAgent.Perception.Providers;

public sealed record ModelTokenUsage(
  int? InputTokens,
  int? OutputTokens,
  int? TotalTokens);
