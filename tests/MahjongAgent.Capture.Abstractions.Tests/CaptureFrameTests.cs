using System.Buffers;
using MahjongAgent.Capture.Abstractions;

namespace MahjongAgent.Capture.Abstractions.Tests;

public sealed class CaptureFrameTests
{
  [Fact]
  public void FrameExposesOnlyTheDeclaredBufferLength()
  {
    using var frame = new CaptureFrame(
      "video:sample",
      12,
      TimeSpan.FromSeconds(3),
      DateTimeOffset.UtcNow,
      2,
      2,
      8,
      CapturePixelFormat.Bgra8,
      MemoryPool<byte>.Shared.Rent(64),
      16);

    Assert.Equal(16, frame.PixelBuffer.Length);
    Assert.Equal(TimeSpan.FromSeconds(3), frame.SourceTimestamp);
  }

  [Fact]
  public void FrameRejectsAnUndersizedStride()
  {
    using var owner = MemoryPool<byte>.Shared.Rent(64);

    Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrame(
      "video:sample",
      0,
      TimeSpan.Zero,
      DateTimeOffset.UtcNow,
      2,
      2,
      7,
      CapturePixelFormat.Bgra8,
      owner,
      16));
  }

  [Fact]
  public void DisposedFrameNoLongerExposesItsBuffer()
  {
    var frame = new CaptureFrame(
      "video:sample",
      0,
      TimeSpan.Zero,
      DateTimeOffset.UtcNow,
      1,
      1,
      4,
      CapturePixelFormat.Bgra8,
      MemoryPool<byte>.Shared.Rent(4),
      4);

    frame.Dispose();

    Assert.Throws<ObjectDisposedException>(() => _ = frame.PixelBuffer);
  }
}
