using System.Text.Json;
using System.Text.Json.Nodes;
using MahjongAgent.Perception.Observations;
using MahjongAgent.Perception.Prompting;

namespace MahjongAgent.Perception.Tests;

public sealed class MahjongObservationParserTests
{
  [Fact]
  public void Parse_accepts_valid_observation_batch()
  {
    using var document = JsonDocument.Parse(ValidObservation);

    var batch = MahjongObservationParser.Parse(
      document.RootElement,
      PerceptionMode.Observe,
      ["frame-42"]);

    var observation = Assert.Single(batch.Observations);
    Assert.Equal(ObservationEntity.Tile, observation.Entity);
    Assert.Equal("5p", Assert.Single(observation.Candidates).Value);
  }

  [Fact]
  public void Parse_rejects_unknown_properties()
  {
    using var document = JsonDocument.Parse(
      ValidObservation.Replace(
        "\"schema_version\": \"mahjong-observation/v1\"",
        "\"schema_version\": \"mahjong-observation/v1\", \"injected\": true",
        StringComparison.Ordinal));

    Assert.Throws<ObservationContractException>(() =>
      MahjongObservationParser.Parse(document.RootElement, PerceptionMode.Observe, ["frame-42"]));
  }

  [Fact]
  public void Parse_rejects_mode_mismatch()
  {
    using var document = JsonDocument.Parse(ValidObservation);

    Assert.Throws<ObservationContractException>(() =>
      MahjongObservationParser.Parse(document.RootElement, PerceptionMode.Recover, ["frame-42"]));
  }

  [Fact]
  public void Parse_rejects_missing_required_mode_instead_of_using_enum_default()
  {
    using var document = JsonDocument.Parse(
      ValidObservation.Replace("\"mode\": \"observe\",", string.Empty, StringComparison.Ordinal));

    Assert.Throws<ObservationContractException>(() =>
      MahjongObservationParser.Parse(document.RootElement, PerceptionMode.Calibrate, ["frame-42"]));
  }

  [Fact]
  public void Parse_rejects_unexpected_evidence_frame()
  {
    var root = JsonNode.Parse(ValidObservation)!.AsObject();
    root["observations"]!.AsArray()[0]!["evidence"]!["frame_ids"] =
      new JsonArray("frame-evil");
    using var document = JsonDocument.Parse(root.ToJsonString());

    Assert.Throws<ObservationContractException>(() =>
      MahjongObservationParser.Parse(document.RootElement, PerceptionMode.Observe, ["frame-42"]));
  }

  [Fact]
  public void Parse_rejects_noncanonical_tile_candidate()
  {
    using var document = JsonDocument.Parse(
      ValidObservation.Replace("\"value\": \"5p\"", "\"value\": \"red five\"", StringComparison.Ordinal));

    var exception = Assert.Throws<ObservationContractException>(() =>
      MahjongObservationParser.Parse(document.RootElement, PerceptionMode.Observe, ["frame-42"]));

    Assert.Contains("canonical tile code", exception.Message, StringComparison.Ordinal);
  }

  private const string ValidObservation =
    """
    {
      "schema_version": "mahjong-observation/v1",
      "mode": "observe",
      "frame_ids": ["frame-42"],
      "observations": [
        {
          "observation_id": "obs-1",
          "entity": "tile",
          "action": "appeared",
          "region": "self_hand",
          "candidates": [
            { "value": "5p", "confidence": 0.97 }
          ],
          "confidence": 0.97,
          "uncertain": false,
          "evidence": {
            "frame_ids": ["frame-42"],
            "visual_cues": ["bottom row, fifth position"]
          }
        }
      ],
      "uncertainties": []
    }
    """;
}
