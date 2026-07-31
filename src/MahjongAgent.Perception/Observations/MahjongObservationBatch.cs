using System.Text.Json.Serialization;
using MahjongAgent.Perception.Prompting;

namespace MahjongAgent.Perception.Observations;

public enum ObservationEntity
{
  Tile,
  PlayerAction,
  Indicator,
  Turn,
  RoundMetadata,
  Meld,
  Discard,
  LayoutRegion,
  Text
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MahjongObservationBatch
{
  public required string SchemaVersion { get; init; }

  public required PerceptionMode Mode { get; init; }

  public required IReadOnlyList<string> FrameIds { get; init; }

  public required IReadOnlyList<MahjongObservation> Observations { get; init; }

  public required IReadOnlyList<ObservationUncertainty> Uncertainties { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MahjongObservation
{
  public required string ObservationId { get; init; }

  public required ObservationEntity Entity { get; init; }

  public required string Action { get; init; }

  public required string Region { get; init; }

  public required IReadOnlyList<ObservationCandidate> Candidates { get; init; }

  public required double Confidence { get; init; }

  public required bool Uncertain { get; init; }

  public required ObservationEvidence Evidence { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ObservationCandidate
{
  public required string Value { get; init; }

  public required double Confidence { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ObservationEvidence
{
  public required IReadOnlyList<string> FrameIds { get; init; }

  public required IReadOnlyList<string> VisualCues { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ObservationUncertainty
{
  public required string Field { get; init; }

  public required string Reason { get; init; }

  public required bool Blocking { get; init; }
}
