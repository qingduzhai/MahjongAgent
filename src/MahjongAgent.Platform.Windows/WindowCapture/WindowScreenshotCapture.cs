using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MahjongAgent.Platform.Windows.WindowCapture;

public sealed class WindowScreenshotCapture
{
  private const uint SourceCopy = 0x00CC0020;
  private const uint CaptureLayeredWindows = 0x40000000;
  private const uint PrintWindowRenderFullContent = 0x00000002;

  public WindowScreenshot Capture(nint windowHandle)
  {
    if (windowHandle == nint.Zero || !IsWindow(windowHandle))
    {
      throw new ArgumentException("The selected window is no longer available.", nameof(windowHandle));
    }

    if (IsIconic(windowHandle))
    {
      throw new InvalidOperationException("请先恢复目标窗口，最小化窗口无法可靠截图。");
    }

    if (!GetWindowRect(windowHandle, out var bounds))
    {
      throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取目标窗口尺寸。");
    }

    var width = bounds.Right - bounds.Left;
    var height = bounds.Bottom - bounds.Top;
    if (width <= 0 || height <= 0)
    {
      throw new InvalidOperationException("目标窗口没有可捕获的画面尺寸。");
    }

    var screenDc = GetDC(nint.Zero);
    if (screenDc == nint.Zero)
    {
      throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建设备上下文。");
    }

    nint memoryDc = nint.Zero;
    nint bitmap = nint.Zero;
    nint previousObject = nint.Zero;
    try
    {
      memoryDc = CreateCompatibleDC(screenDc);
      bitmap = CreateCompatibleBitmap(screenDc, width, height);
      if (memoryDc == nint.Zero || bitmap == nint.Zero)
      {
        throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建截图缓冲区。");
      }

      previousObject = SelectObject(memoryDc, bitmap);
      var captured = PrintWindow(windowHandle, memoryDc, PrintWindowRenderFullContent);
      if (!captured)
      {
        captured = BitBlt(
          memoryDc,
          0,
          0,
          width,
          height,
          screenDc,
          bounds.Left,
          bounds.Top,
          SourceCopy | CaptureLayeredWindows);
      }

      if (!captured)
      {
        throw new Win32Exception(Marshal.GetLastWin32Error(), "目标窗口截图失败。");
      }

      var preview = Imaging.CreateBitmapSourceFromHBitmap(
        bitmap,
        nint.Zero,
        Int32Rect.Empty,
        BitmapSizeOptions.FromEmptyOptions());
      preview.Freeze();

      return new WindowScreenshot(
        preview,
        EncodeForProvider(preview),
        preview.PixelWidth,
        preview.PixelHeight,
        DateTimeOffset.UtcNow);
    }
    finally
    {
      if (previousObject != nint.Zero && memoryDc != nint.Zero)
      {
        _ = SelectObject(memoryDc, previousObject);
      }

      if (bitmap != nint.Zero)
      {
        _ = DeleteObject(bitmap);
      }

      if (memoryDc != nint.Zero)
      {
        _ = DeleteDC(memoryDc);
      }

      _ = ReleaseDC(nint.Zero, screenDc);
    }
  }

  private static byte[] EncodeForProvider(BitmapSource source)
  {
    const int maximumDimension = 1600;
    var largestDimension = Math.Max(source.PixelWidth, source.PixelHeight);
    BitmapSource encodedSource = source;
    if (largestDimension > maximumDimension)
    {
      var scale = maximumDimension / (double)largestDimension;
      var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
      resized.Freeze();
      encodedSource = resized;
    }

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(encodedSource));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeRect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsWindow(nint handle);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsIconic(nint handle);

  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetWindowRect(nint handle, out NativeRect rectangle);

  [DllImport("user32.dll")]
  private static extern nint GetDC(nint handle);

  [DllImport("user32.dll")]
  private static extern int ReleaseDC(nint handle, nint deviceContext);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool PrintWindow(nint handle, nint deviceContext, uint flags);

  [DllImport("gdi32.dll", SetLastError = true)]
  private static extern nint CreateCompatibleDC(nint deviceContext);

  [DllImport("gdi32.dll", SetLastError = true)]
  private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

  [DllImport("gdi32.dll")]
  private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

  [DllImport("gdi32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool DeleteObject(nint graphicsObject);

  [DllImport("gdi32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool DeleteDC(nint deviceContext);

  [DllImport("gdi32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool BitBlt(
    nint destinationDeviceContext,
    int x,
    int y,
    int width,
    int height,
    nint sourceDeviceContext,
    int sourceX,
    int sourceY,
    uint rasterOperation);
}
