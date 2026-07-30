namespace MahjongAgent.Perception.Providers;

public sealed record PerceptionRequest
{
  public PerceptionRequest(
    string systemPrompt,
    string userPrompt,
    IEnumerable<PerceptionImage> images,
    StructuredOutputSchema outputSchema,
    string? correlationId = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
    ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
    ArgumentNullException.ThrowIfNull(images);
    ArgumentNullException.ThrowIfNull(outputSchema);

    var imageArray = images.ToArray();
    if (imageArray.Length is 0)
    {
      throw new ArgumentException("At least one image is required for a perception request.", nameof(images));
    }

    if (correlationId is not null &&
        (string.IsNullOrWhiteSpace(correlationId) ||
         correlationId.Length > 128 ||
         correlationId.Contains('\r', StringComparison.Ordinal) ||
         correlationId.Contains('\n', StringComparison.Ordinal)))
    {
      throw new ArgumentException(
        "Correlation IDs must be non-empty, at most 128 characters and cannot contain newlines.",
        nameof(correlationId));
    }

    SystemPrompt = systemPrompt;
    UserPrompt = userPrompt;
    Images = imageArray;
    OutputSchema = outputSchema;
    CorrelationId = correlationId;
  }

  public string SystemPrompt { get; }

  public string UserPrompt { get; }

  public IReadOnlyList<PerceptionImage> Images { get; }

  public StructuredOutputSchema OutputSchema { get; }

  public string? CorrelationId { get; }
}
