using System.Text.Json;
using System.Text.RegularExpressions;

namespace MahjongAgent.Perception.Providers;

public sealed partial record StructuredOutputSchema
{
  public StructuredOutputSchema(string name, JsonElement schema)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);

    if (!SchemaNamePattern().IsMatch(name))
    {
      throw new ArgumentException(
        "Schema names may contain only letters, numbers, underscores and hyphens, with a maximum length of 64.",
        nameof(name));
    }

    if (schema.ValueKind is not JsonValueKind.Object)
    {
      throw new ArgumentException("The JSON schema root must be an object.", nameof(schema));
    }

    Name = name;
    Schema = schema.Clone();
  }

  public string Name { get; }

  public JsonElement Schema { get; }

  [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
  private static partial Regex SchemaNamePattern();
}
