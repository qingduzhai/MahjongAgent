using MahjongAgent.Platform.Windows.WindowCapture;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MahjongAgent.Platform.Windows.Tests;

public sealed class WindowCaptureTests
{
  [Fact]
  public void WindowCandidateBuildsReadableDisplayLabel()
  {
    var candidate = new WindowCandidate((nint)42, "完整牌局", "player");

    Assert.Equal("完整牌局  ·  player", candidate.DisplayLabel);
  }

  [Fact]
  public void CaptureRejectsMissingWindow()
  {
    var capture = new WindowScreenshotCapture();

    Assert.Throws<ArgumentException>(() => capture.Capture(nint.Zero));
  }

  [Fact]
  public void CaptureProducesPngForVisibleWpfWindow()
  {
    WindowScreenshot? screenshot = null;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try
      {
        var window = new Window
        {
          Width = 320,
          Height = 240,
          Left = -32000,
          Top = -32000,
          ShowInTaskbar = false,
          Content = new Border { Background = Brushes.DarkGreen }
        };
        window.Show();
        screenshot = new WindowScreenshotCapture().Capture(new WindowInteropHelper(window).Handle);
        window.Close();
      }
      catch (Exception exception)
      {
        failure = exception;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The WPF capture thread timed out.");

    Assert.Null(failure);
    Assert.NotNull(screenshot);
    Assert.True(screenshot.PixelWidth > 0);
    Assert.True(screenshot.PixelHeight > 0);
    Assert.Equal([0x89, 0x50, 0x4E, 0x47], screenshot.PngBytes[..4]);
  }
}
