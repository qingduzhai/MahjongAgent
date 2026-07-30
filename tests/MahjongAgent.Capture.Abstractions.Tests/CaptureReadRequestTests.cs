using MahjongAgent.Capture.Abstractions;

namespace MahjongAgent.Capture.Abstractions.Tests;

public sealed class CaptureReadRequestTests
{
  private static readonly CaptureSourceDescriptor WindowSource = new(
    "window:42",
    "Mahjong window",
    CaptureSourceKind.Window,
    CaptureSourceCapabilities.RealTime);

  private static readonly CaptureSourceDescriptor VideoSource = new(
    "video:sample",
    "sample.mp4",
    CaptureSourceKind.VideoFile,
    CaptureSourceCapabilities.Finite |
    CaptureSourceCapabilities.Seekable |
    CaptureSourceCapabilities.RealTime |
    CaptureSourceCapabilities.FastReplay);

  [Fact]
  public void WindowSourceAcceptsRealTimeReading()
  {
    new CaptureReadRequest().ValidateFor(WindowSource);
  }

  [Fact]
  public void WindowSourceRejectsTimelineRange()
  {
    var request = new CaptureReadRequest { StartPosition = TimeSpan.FromSeconds(1) };

    Assert.Throws<NotSupportedException>(() => request.ValidateFor(WindowSource));
  }

  [Fact]
  public void WindowSourceRejectsFastReplay()
  {
    var request = new CaptureReadRequest { PacingMode = CapturePacingMode.AsFastAsPossible };

    Assert.Throws<NotSupportedException>(() => request.ValidateFor(WindowSource));
  }

  [Fact]
  public void VideoSourceAcceptsRangeAndFastReplay()
  {
    var request = new CaptureReadRequest
    {
      StartPosition = TimeSpan.FromMinutes(2),
      EndPosition = TimeSpan.FromMinutes(5),
      MinimumFrameInterval = TimeSpan.FromMilliseconds(500),
      PacingMode = CapturePacingMode.AsFastAsPossible
    };

    request.ValidateFor(VideoSource);
  }

  [Fact]
  public void RequestRejectsReversedRange()
  {
    var request = new CaptureReadRequest
    {
      StartPosition = TimeSpan.FromSeconds(10),
      EndPosition = TimeSpan.FromSeconds(9)
    };

    Assert.Throws<ArgumentException>(() => request.ValidateFor(VideoSource));
  }
}
