namespace MahjongAgent.Providers.OpenAICompatible;

[Flags]
public enum OpenAiCompatibleCapabilities
{
  None = 0,
  Vision = 1 << 0,
  JsonSchema = 1 << 1,
  JsonObject = 1 << 2,
  Embeddings = 1 << 3,
  Streaming = 1 << 4
}
