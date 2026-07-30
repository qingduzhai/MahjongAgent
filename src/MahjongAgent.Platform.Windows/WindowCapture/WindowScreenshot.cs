using System.Windows.Media.Imaging;

namespace MahjongAgent.Platform.Windows.WindowCapture;

public sealed record WindowScreenshot(
  BitmapSource Preview,
  byte[] PngBytes,
  int PixelWidth,
  int PixelHeight,
  DateTimeOffset CapturedAtUtc);
