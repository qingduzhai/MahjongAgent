using System.Buffers;
using MahjongAgent.Capture.Abstractions;

namespace MahjongAgent.Capture.Abstractions.Tests;

public sealed class CaptureFrameTimeTests
{
  [Fact]
  public void FrameRequiresUtcObservationTime()
  {
    using var owner = MemoryPool<byte>.Shared.Rent(4);

    Assert.Throws<ArgumentException>(() => new CaptureFrame(
      "window:42",
      0,
      TimeSpan.Zero,
      new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(8)),
      1,
      1,
      4,
      CapturePixelFormat.Bgra8,
      owner,
      4));
  }
}
