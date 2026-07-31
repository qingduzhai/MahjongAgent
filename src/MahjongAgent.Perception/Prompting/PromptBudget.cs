namespace MahjongAgent.Perception.Prompting;

public sealed record PromptBudget
{
  public PromptBudget(
    int maxContextCharacters = 12_000,
    int maxKnownFacts = 48,
    int maxUncertainties = 24,
    int maxRecentEvents = 24,
    int maxChangedRegions = 16)
  {
    if (maxContextCharacters is < 512 or > 100_000)
    {
      throw new ArgumentOutOfRangeException(
        nameof(maxContextCharacters),
        "Context character budget must be between 512 and 100000.");
    }

    MaxKnownFacts = ValidateCount(maxKnownFacts, nameof(maxKnownFacts));
    MaxUncertainties = ValidateCount(maxUncertainties, nameof(maxUncertainties));
    MaxRecentEvents = ValidateCount(maxRecentEvents, nameof(maxRecentEvents));
    MaxChangedRegions = ValidateCount(maxChangedRegions, nameof(maxChangedRegions));
    MaxContextCharacters = maxContextCharacters;
  }

  public int MaxContextCharacters { get; }

  public int MaxKnownFacts { get; }

  public int MaxUncertainties { get; }

  public int MaxRecentEvents { get; }

  public int MaxChangedRegions { get; }

  private static int ValidateCount(int value, string parameterName)
  {
    if (value is < 0 or > 512)
    {
      throw new ArgumentOutOfRangeException(
        parameterName,
        "Prompt item limits must be between 0 and 512.");
    }

    return value;
  }
}

public sealed record PromptBudgetUsage(
  int ContextCharacters,
  int IncludedKnownFacts,
  int DroppedKnownFacts,
  int IncludedUncertainties,
  int DroppedUncertainties,
  int IncludedRecentEvents,
  int DroppedRecentEvents,
  int IncludedChangedRegions,
  int DroppedChangedRegions);

public sealed class PromptBudgetExceededException(string message) : InvalidOperationException(message);
