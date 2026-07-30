using MahjongAgent.Capture.Abstractions;

namespace MahjongAgent.Capture.Abstractions.Tests;

public sealed class CaptureSourceDescriptorTests
{
  [Fact]
  public void WindowSourceRequiresLiveOnlyCapabilities()
  {
    Assert.Throws<ArgumentException>(() => new CaptureSourceDescriptor(
      "window:42",
      "Mahjong window",
      CaptureSourceKind.Window,
      CaptureSourceCapabilities.RealTime | CaptureSourceCapabilities.Seekable));
  }

  [Fact]
  public void VideoSourceRequiresCompleteTimelineCapabilities()
  {
    Assert.Throws<ArgumentException>(() => new CaptureSourceDescriptor(
      "video:sample",
      "sample.mp4",
      CaptureSourceKind.VideoFile,
      CaptureSourceCapabilities.Finite | CaptureSourceCapabilities.Seekable));
  }
}
