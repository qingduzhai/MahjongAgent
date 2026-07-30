using System.Buffers;

namespace MahjongAgent.Capture.Abstractions;

public sealed class CaptureFrame : IDisposable
{
  private IMemoryOwner<byte>? _pixelBuffer;
  private readonly int _bufferLength;

  public CaptureFrame(
    string sourceId,
    long sequenceNumber,
    TimeSpan sourceTimestamp,
    DateTimeOffset observedAtUtc,
    int pixelWidth,
    int pixelHeight,
    int rowStride,
    CapturePixelFormat pixelFormat,
    IMemoryOwner<byte> pixelBuffer,
    int bufferLength)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
    ArgumentNullException.ThrowIfNull(pixelBuffer);

    if (sequenceNumber < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sequenceNumber));
    }

    if (sourceTimestamp < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(sourceTimestamp));
    }

    if (observedAtUtc.Offset != TimeSpan.Zero)
    {
      throw new ArgumentException("ObservedAtUtc must use the UTC offset.", nameof(observedAtUtc));
    }

    if (pixelWidth <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(pixelWidth));
    }

    if (pixelHeight <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(pixelHeight));
    }

    if (!Enum.IsDefined(pixelFormat))
    {
      throw new ArgumentOutOfRangeException(nameof(pixelFormat));
    }

    var minimumStride = checked(pixelWidth * BytesPerPixel(pixelFormat));
    if (rowStride < minimumStride)
    {
      throw new ArgumentOutOfRangeException(nameof(rowStride));
    }

    var minimumLength = checked(rowStride * pixelHeight);
    if (bufferLength < minimumLength || bufferLength > pixelBuffer.Memory.Length)
    {
      throw new ArgumentOutOfRangeException(nameof(bufferLength));
    }

    SourceId = sourceId;
    SequenceNumber = sequenceNumber;
    SourceTimestamp = sourceTimestamp;
    ObservedAtUtc = observedAtUtc;
    PixelWidth = pixelWidth;
    PixelHeight = pixelHeight;
    RowStride = rowStride;
    PixelFormat = pixelFormat;
    _pixelBuffer = pixelBuffer;
    _bufferLength = bufferLength;
  }

  public string SourceId { get; }

  public long SequenceNumber { get; }

  public TimeSpan SourceTimestamp { get; }

  public DateTimeOffset ObservedAtUtc { get; }

  public int PixelWidth { get; }

  public int PixelHeight { get; }

  public int RowStride { get; }

  public CapturePixelFormat PixelFormat { get; }

  public ReadOnlyMemory<byte> PixelBuffer =>
    (_pixelBuffer ?? throw new ObjectDisposedException(nameof(CaptureFrame)))
    .Memory[.._bufferLength];

  public void Dispose()
  {
    _pixelBuffer?.Dispose();
    _pixelBuffer = null;
  }

  private static int BytesPerPixel(CapturePixelFormat pixelFormat) => pixelFormat switch
  {
    CapturePixelFormat.Bgra8 => 4,
    CapturePixelFormat.Rgba8 => 4,
    CapturePixelFormat.Gray8 => 1,
    _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat))
  };
}
