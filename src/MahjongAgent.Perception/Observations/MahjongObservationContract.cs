using System.Text.Json;
using MahjongAgent.Perception.Providers;

namespace MahjongAgent.Perception.Observations;

public static class MahjongObservationContract
{
  public const string SchemaVersion = "mahjong-observation/v1";

  public static StructuredOutputSchema OutputSchema { get; } = CreateOutputSchema();

  private static StructuredOutputSchema CreateOutputSchema()
  {
    using var document = JsonDocument.Parse(
      """
      {
        "type": "object",
        "additionalProperties": false,
        "required": ["schema_version", "mode", "frame_ids", "observations", "uncertainties"],
        "properties": {
          "schema_version": { "type": "string", "enum": ["mahjong-observation/v1"] },
          "mode": { "type": "string", "enum": ["calibrate", "observe", "recover"] },
          "frame_ids": {
            "type": "array",
            "minItems": 1,
            "maxItems": 8,
            "uniqueItems": true,
            "items": { "type": "string", "minLength": 1, "maxLength": 128 }
          },
          "observations": {
            "type": "array",
            "maxItems": 256,
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["observation_id", "entity", "action", "region", "candidates", "confidence", "uncertain", "evidence"],
              "properties": {
                "observation_id": { "type": "string", "minLength": 1, "maxLength": 64 },
                "entity": {
                  "type": "string",
                  "enum": ["tile", "player_action", "indicator", "turn", "round_metadata", "meld", "discard", "layout_region", "text"]
                },
                "action": { "type": "string", "minLength": 1, "maxLength": 64 },
                "region": { "type": "string", "minLength": 1, "maxLength": 64 },
                "candidates": {
                  "type": "array",
                  "maxItems": 16,
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["value", "confidence"],
                    "properties": {
                      "value": { "type": "string", "minLength": 1, "maxLength": 256 },
                      "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                    }
                  }
                },
                "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                "uncertain": { "type": "boolean" },
                "evidence": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["frame_ids", "visual_cues"],
                  "properties": {
                    "frame_ids": {
                      "type": "array",
                      "minItems": 1,
                      "maxItems": 8,
                      "uniqueItems": true,
                      "items": { "type": "string", "minLength": 1, "maxLength": 128 }
                    },
                    "visual_cues": {
                      "type": "array",
                      "maxItems": 16,
                      "items": { "type": "string", "minLength": 1, "maxLength": 512 }
                    }
                  }
                }
              }
            }
          },
          "uncertainties": {
            "type": "array",
            "maxItems": 64,
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["field", "reason", "blocking"],
              "properties": {
                "field": { "type": "string", "minLength": 1, "maxLength": 64 },
                "reason": { "type": "string", "minLength": 1, "maxLength": 512 },
                "blocking": { "type": "boolean" }
              }
            }
          }
        }
      }
      """);
    return new StructuredOutputSchema("mahjong_observation_v1", document.RootElement);
  }
}
