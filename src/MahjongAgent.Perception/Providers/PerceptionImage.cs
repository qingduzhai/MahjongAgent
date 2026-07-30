namespace MahjongAgent.Perception.Providers;

public sealed record PerceptionImage
{
  public PerceptionImage(string mediaType, ReadOnlyMemory<byte> data)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

    if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Length is <= 6 ||
        mediaType.Any(character =>
          !char.IsAsciiLetterOrDigit(character) && character is not '/' and not '+' and not '-' and not '.'))
    {
      throw new ArgumentException("The media type must be an image type.", nameof(mediaType));
    }

    if (data.IsEmpty)
    {
      throw new ArgumentException("Image data cannot be empty.", nameof(data));
    }

    MediaType = mediaType.ToLowerInvariant();
    Data = data.ToArray();
  }

  public string MediaType { get; }

  public ReadOnlyMemory<byte> Data { get; }
}
