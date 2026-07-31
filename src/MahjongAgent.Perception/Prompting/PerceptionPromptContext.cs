namespace MahjongAgent.Perception.Prompting;

public sealed record KnownFact
{
  public KnownFact(string key, string value, int priority = 50, bool isRequired = false)
  {
    Key = PromptContextGuard.Identifier(key, nameof(key));
    Value = PromptContextGuard.Text(value, nameof(value));
    Priority = PromptContextGuard.Priority(priority, nameof(priority));
    IsRequired = isRequired;
  }

  public string Key { get; }

  public string Value { get; }

  public int Priority { get; }

  public bool IsRequired { get; }
}

public sealed record Uncertainty
{
  public Uncertainty(string field, string reason, bool isBlocking, int priority = 50)
  {
    Field = PromptContextGuard.Identifier(field, nameof(field));
    Reason = PromptContextGuard.Text(reason, nameof(reason));
    IsBlocking = isBlocking;
    Priority = PromptContextGuard.Priority(priority, nameof(priority));
  }

  public string Field { get; }

  public string Reason { get; }

  public bool IsBlocking { get; }

  public int Priority { get; }
}

public sealed record RecentEventSummary
{
  public RecentEventSummary(long sequence, string eventType, string summary, int priority = 50)
  {
    if (sequence < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sequence));
    }

    Sequence = sequence;
    EventType = PromptContextGuard.Identifier(eventType, nameof(eventType));
    Summary = PromptContextGuard.Text(summary, nameof(summary));
    Priority = PromptContextGuard.Priority(priority, nameof(priority));
  }

  public long Sequence { get; }

  public string EventType { get; }

  public string Summary { get; }

  public int Priority { get; }
}

public sealed record ChangedRegion
{
  public ChangedRegion(string regionId, double changeScore, string summary, int priority = 50)
  {
    if (!double.IsFinite(changeScore) || changeScore is < 0 or > 1)
    {
      throw new ArgumentOutOfRangeException(nameof(changeScore), "Change score must be between 0 and 1.");
    }

    RegionId = PromptContextGuard.Identifier(regionId, nameof(regionId));
    ChangeScore = changeScore;
    Summary = PromptContextGuard.Text(summary, nameof(summary));
    Priority = PromptContextGuard.Priority(priority, nameof(priority));
  }

  public string RegionId { get; }

  public double ChangeScore { get; }

  public string Summary { get; }

  public int Priority { get; }
}

public sealed record PerceptionPromptContext
{
  public PerceptionPromptContext(
    string ruleProfileVersion,
    long gameStateRevision,
    string visualProfileVersion,
    IEnumerable<string> frameIds,
    IEnumerable<KnownFact>? knownFacts = null,
    IEnumerable<Uncertainty>? uncertainties = null,
    IEnumerable<RecentEventSummary>? recentEvents = null,
    IEnumerable<ChangedRegion>? changedRegions = null)
  {
    if (gameStateRevision < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(gameStateRevision));
    }

    ArgumentNullException.ThrowIfNull(frameIds);
    var normalizedFrameIds = frameIds
      .Select(frameId => PromptContextGuard.Identifier(frameId, nameof(frameIds), 128))
      .ToArray();
    if (normalizedFrameIds.Length is 0)
    {
      throw new ArgumentException("At least one frame ID is required.", nameof(frameIds));
    }

    if (normalizedFrameIds.Distinct(StringComparer.Ordinal).Count() != normalizedFrameIds.Length)
    {
      throw new ArgumentException("Frame IDs must be unique.", nameof(frameIds));
    }

    RuleProfileVersion = PromptContextGuard.Identifier(
      ruleProfileVersion,
      nameof(ruleProfileVersion),
      128);
    GameStateRevision = gameStateRevision;
    VisualProfileVersion = PromptContextGuard.Identifier(
      visualProfileVersion,
      nameof(visualProfileVersion),
      128);
    FrameIds = normalizedFrameIds;
    KnownFacts = (knownFacts ?? []).ToArray();
    Uncertainties = (uncertainties ?? []).ToArray();
    RecentEvents = (recentEvents ?? []).ToArray();
    ChangedRegions = (changedRegions ?? []).ToArray();
  }

  public string RuleProfileVersion { get; }

  public long GameStateRevision { get; }

  public string VisualProfileVersion { get; }

  public IReadOnlyList<string> FrameIds { get; }

  public IReadOnlyList<KnownFact> KnownFacts { get; }

  public IReadOnlyList<Uncertainty> Uncertainties { get; }

  public IReadOnlyList<RecentEventSummary> RecentEvents { get; }

  public IReadOnlyList<ChangedRegion> ChangedRegions { get; }
}

internal static class PromptContextGuard
{
  public static string Identifier(string value, string parameterName, int maxLength = 64)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    if (value.Length > maxLength ||
        !char.IsAsciiLetterOrDigit(value[0]) ||
        value.Any(character =>
          !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
    {
      throw new ArgumentException(
        $"'{parameterName}' must contain only ASCII letters, digits, dots, underscores or hyphens and be at most {maxLength} characters.",
        parameterName);
    }

    return value;
  }

  public static string Text(string value, string parameterName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    if (value.Length > 4096)
    {
      throw new ArgumentException($"'{parameterName}' cannot exceed 4096 characters.", parameterName);
    }

    return value;
  }

  public static int Priority(int value, string parameterName)
  {
    if (value is < 0 or > 100)
    {
      throw new ArgumentOutOfRangeException(parameterName, "Priority must be between 0 and 100.");
    }

    return value;
  }
}
