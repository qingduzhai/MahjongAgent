namespace MahjongAgent.Perception.Resolution;

public sealed record ObservationResolutionOptions
{
  public ObservationResolutionOptions(
    double minimumConfidence = 0.9,
    int requiredConfirmations = 2)
  {
    if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
    {
      throw new ArgumentOutOfRangeException(
        nameof(minimumConfidence),
        "Minimum confidence must be between 0 and 1.");
    }

    if (requiredConfirmations is < 1 or > 8)
    {
      throw new ArgumentOutOfRangeException(
        nameof(requiredConfirmations),
        "Required confirmations must be between one and eight.");
    }

    MinimumConfidence = minimumConfidence;
    RequiredConfirmations = requiredConfirmations;
  }

  public double MinimumConfidence { get; }

  public int RequiredConfirmations { get; }
}
