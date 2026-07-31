using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MahjongAgent.Core.Tiles;
using MahjongAgent.Perception.Prompting;

namespace MahjongAgent.Perception.Observations;

public static partial class MahjongObservationParser
{
  private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

  public static MahjongObservationBatch Parse(
    JsonElement output,
    PerceptionMode expectedMode,
    IReadOnlyCollection<string> expectedFrameIds)
  {
    if (output.ValueKind is not JsonValueKind.Object)
    {
      throw new ObservationContractException("Observation output must be a JSON object.");
    }

    ArgumentNullException.ThrowIfNull(expectedFrameIds);
    MahjongObservationBatch batch;
    try
    {
      batch = output.Deserialize<MahjongObservationBatch>(SerializerOptions) ??
        throw new ObservationContractException("Observation output was empty.");
    }
    catch (JsonException exception)
    {
      throw new ObservationContractException(
        "Observation output does not match the v1 typed contract.",
        exception);
    }

    Validate(batch, expectedMode, expectedFrameIds);
    return batch;
  }

  private static void Validate(
    MahjongObservationBatch batch,
    PerceptionMode expectedMode,
    IReadOnlyCollection<string> expectedFrameIds)
  {
    if (!string.Equals(batch.SchemaVersion, MahjongObservationContract.SchemaVersion, StringComparison.Ordinal))
    {
      throw new ObservationContractException(
        $"Unsupported observation schema version '{batch.SchemaVersion}'.");
    }

    if (batch.Mode != expectedMode)
    {
      throw new ObservationContractException(
        $"Observation mode '{batch.Mode}' does not match requested mode '{expectedMode}'.");
    }

    ValidateFrameIds(batch.FrameIds, expectedFrameIds, "batch");
    if (batch.Observations is null || batch.Observations.Count > 256)
    {
      throw new ObservationContractException("Observation count exceeds the v1 contract.");
    }

    if (batch.Uncertainties is null || batch.Uncertainties.Count > 64)
    {
      throw new ObservationContractException("Uncertainty count exceeds the v1 contract.");
    }

    var observationIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var observation in batch.Observations)
    {
      ValidateIdentifier(observation.ObservationId, "observation_id");
      if (!observationIds.Add(observation.ObservationId))
      {
        throw new ObservationContractException(
          $"Duplicate observation ID '{observation.ObservationId}'.");
      }

      ValidateIdentifier(observation.Action, "action");
      ValidateIdentifier(observation.Region, "region");
      ValidateConfidence(observation.Confidence, "observation confidence");
      if (observation.Candidates is null || observation.Candidates.Count > 16)
      {
        throw new ObservationContractException(
          $"Observation '{observation.ObservationId}' has too many candidates.");
      }

      if (!observation.Uncertain && observation.Candidates.Count is 0)
      {
        throw new ObservationContractException(
          $"Observation '{observation.ObservationId}' has no candidate but is not marked uncertain.");
      }

      var candidateValues = new HashSet<string>(StringComparer.Ordinal);
      foreach (var candidate in observation.Candidates)
      {
        ValidateText(candidate.Value, "candidate value", 256);
        ValidateConfidence(candidate.Confidence, "candidate confidence");
        if (!candidateValues.Add(candidate.Value))
        {
          throw new ObservationContractException(
            $"Observation '{observation.ObservationId}' contains duplicate candidate '{candidate.Value}'.");
        }

        if (observation.Entity is ObservationEntity.Tile or ObservationEntity.Indicator &&
            !TileType.TryParse(candidate.Value, out _))
        {
          throw new ObservationContractException(
            $"'{candidate.Value}' is not a canonical tile code.");
        }
      }

      if (observation.Evidence is null)
      {
        throw new ObservationContractException(
          $"Observation '{observation.ObservationId}' has no evidence.");
      }

      ValidateFrameIds(observation.Evidence.FrameIds, batch.FrameIds, "evidence");
      if (observation.Evidence.VisualCues is null || observation.Evidence.VisualCues.Count > 16)
      {
        throw new ObservationContractException("Evidence contains too many visual cues.");
      }

      foreach (var cue in observation.Evidence.VisualCues)
      {
        ValidateText(cue, "visual cue", 512);
      }
    }

    foreach (var uncertainty in batch.Uncertainties)
    {
      ValidateIdentifier(uncertainty.Field, "uncertainty field");
      ValidateText(uncertainty.Reason, "uncertainty reason", 512);
    }
  }

  private static void ValidateFrameIds(
    IReadOnlyCollection<string>? actual,
    IReadOnlyCollection<string> allowed,
    string scope)
  {
    if (actual is null || actual.Count is 0 || actual.Count > 8)
    {
      throw new ObservationContractException(
        $"The {scope} must reference between one and eight frames.");
    }

    var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var frameId in actual)
    {
      ValidateIdentifier(frameId, "frame_id", 128);
      if (!seen.Add(frameId))
      {
        throw new ObservationContractException($"Duplicate {scope} frame ID '{frameId}'.");
      }

      if (!allowedSet.Contains(frameId))
      {
        throw new ObservationContractException(
          $"The {scope} references unexpected frame ID '{frameId}'.");
      }
    }

    if (scope == "batch" && (actual.Count != allowedSet.Count || !allowedSet.SetEquals(seen)))
    {
      throw new ObservationContractException(
        "Batch frame IDs must exactly match the frames supplied with the request.");
    }
  }

  private static void ValidateIdentifier(string? value, string fieldName, int maxLength = 64)
  {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > maxLength ||
        !IdentifierPattern().IsMatch(value))
    {
      throw new ObservationContractException(
        $"'{fieldName}' is not a valid protocol identifier.");
    }
  }

  private static void ValidateText(string? value, string fieldName, int maxLength)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
    {
      throw new ObservationContractException(
        $"'{fieldName}' must be non-empty and at most {maxLength} characters.");
    }
  }

  private static void ValidateConfidence(double value, string fieldName)
  {
    if (!double.IsFinite(value) || value is < 0 or > 1)
    {
      throw new ObservationContractException($"{fieldName} must be between 0 and 1.");
    }
  }

  private static JsonSerializerOptions CreateSerializerOptions()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
      UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false));
    return options;
  }

  [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
  private static partial Regex IdentifierPattern();
}

public sealed class ObservationContractException : FormatException
{
  public ObservationContractException(string message)
    : base(message)
  {
  }

  public ObservationContractException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
