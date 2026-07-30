namespace MahjongAgent.Capture.Abstractions;

public sealed record CaptureSourceDescriptor
{
  public CaptureSourceDescriptor(
    string id,
    string displayName,
    CaptureSourceKind kind,
    CaptureSourceCapabilities capabilities)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(id);
    ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

    if (!Enum.IsDefined(kind))
    {
      throw new ArgumentOutOfRangeException(nameof(kind));
    }

    if ((capabilities & ~AllCapabilities) is not 0)
    {
      throw new ArgumentOutOfRangeException(nameof(capabilities));
    }

    if (kind is CaptureSourceKind.Window &&
        (capabilities != CaptureSourceCapabilities.RealTime))
    {
      throw new ArgumentException(
        "A window source must be real-time, non-finite and non-seekable.",
        nameof(capabilities));
    }

    if (kind is CaptureSourceKind.VideoFile &&
        !capabilities.HasFlag(RequiredVideoCapabilities))
    {
      throw new ArgumentException(
        "A video file source must be finite, seekable and support real-time and fast replay.",
        nameof(capabilities));
    }

    Id = id;
    DisplayName = displayName;
    Kind = kind;
    Capabilities = capabilities;
  }

  public string Id { get; }

  public string DisplayName { get; }

  public CaptureSourceKind Kind { get; }

  public CaptureSourceCapabilities Capabilities { get; }

  private const CaptureSourceCapabilities AllCapabilities =
    CaptureSourceCapabilities.Finite |
    CaptureSourceCapabilities.Seekable |
    CaptureSourceCapabilities.RealTime |
    CaptureSourceCapabilities.FastReplay;

  private const CaptureSourceCapabilities RequiredVideoCapabilities = AllCapabilities;
}
