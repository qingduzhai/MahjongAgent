using MahjongAgent.Perception.Providers;

namespace MahjongAgent.Perception.Prompting;

public sealed record PromptPackage
{
  internal PromptPackage(
    string promptVersion,
    string schemaVersion,
    PerceptionMode mode,
    string ruleProfileVersion,
    long gameStateRevision,
    string visualProfileVersion,
    IReadOnlyList<string> frameIds,
    IReadOnlyList<KnownFact> knownFacts,
    IReadOnlyList<Uncertainty> uncertainties,
    IReadOnlyList<RecentEventSummary> recentEvents,
    IReadOnlyList<ChangedRegion> changedRegions,
    string systemPrompt,
    string userPrompt,
    PromptBudget budget,
    PromptBudgetUsage budgetUsage,
    StructuredOutputSchema outputSchema)
  {
    PromptVersion = promptVersion;
    SchemaVersion = schemaVersion;
    Mode = mode;
    RuleProfileVersion = ruleProfileVersion;
    GameStateRevision = gameStateRevision;
    VisualProfileVersion = visualProfileVersion;
    FrameIds = frameIds;
    KnownFacts = knownFacts;
    Uncertainties = uncertainties;
    RecentEvents = recentEvents;
    ChangedRegions = changedRegions;
    SystemPrompt = systemPrompt;
    UserPrompt = userPrompt;
    Budget = budget;
    BudgetUsage = budgetUsage;
    OutputSchema = outputSchema;
  }

  public string PromptVersion { get; }

  public string SchemaVersion { get; }

  public PerceptionMode Mode { get; }

  public string RuleProfileVersion { get; }

  public long GameStateRevision { get; }

  public string VisualProfileVersion { get; }

  public string? MemoryVersion => null;

  public IReadOnlyList<string> RetrievedMemoryIds => [];

  public IReadOnlyList<string> FrameIds { get; }

  public IReadOnlyList<KnownFact> KnownFacts { get; }

  public IReadOnlyList<Uncertainty> Uncertainties { get; }

  public IReadOnlyList<RecentEventSummary> RecentEvents { get; }

  public IReadOnlyList<ChangedRegion> ChangedRegions { get; }

  public string SystemPrompt { get; }

  public string UserPrompt { get; }

  public PromptBudget Budget { get; }

  public PromptBudgetUsage BudgetUsage { get; }

  public StructuredOutputSchema OutputSchema { get; }

  public PerceptionRequest CreateRequest(
    IEnumerable<PerceptionImage> images,
    string? correlationId = null)
  {
    ArgumentNullException.ThrowIfNull(images);
    var imageArray = images.ToArray();
    if (imageArray.Length != FrameIds.Count)
    {
      throw new ArgumentException(
        "The number of images must match the number of frame IDs in the prompt package.",
        nameof(images));
    }

    return new PerceptionRequest(
      SystemPrompt,
      UserPrompt,
      imageArray,
      OutputSchema,
      correlationId);
  }
}
