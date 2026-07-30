using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MahjongAgent.Platform.Windows.WindowCapture;

public sealed class WindowCatalog
{
  public IReadOnlyList<WindowCandidate> ListVisibleWindows()
  {
    var windows = new List<WindowCandidate>();
    var currentProcessId = Environment.ProcessId;
    var shellWindow = GetShellWindow();

    EnumWindows((handle, parameter) =>
    {
      if (handle == shellWindow ||
          !IsWindowVisible(handle) ||
          IsIconic(handle) ||
          IsCloaked(handle))
      {
        return true;
      }

      var titleLength = GetWindowTextLength(handle);
      if (titleLength <= 0)
      {
        return true;
      }

      var title = new StringBuilder(titleLength + 1);
      _ = GetWindowText(handle, title, title.Capacity);
      var normalizedTitle = title.ToString().Trim();
      if (normalizedTitle.Length == 0)
      {
        return true;
      }

      _ = GetWindowThreadProcessId(handle, out var processId);
      if (processId == currentProcessId)
      {
        return true;
      }

      windows.Add(new WindowCandidate(
        handle,
        normalizedTitle,
        TryGetProcessName(processId)));
      return true;
    }, nint.Zero);

    return windows
      .OrderBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase)
      .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
      .ToArray();
  }

  private static string TryGetProcessName(uint processId)
  {
    try
    {
      using var process = Process.GetProcessById(checked((int)processId));
      return process.ProcessName;
    }
    catch (Exception exception) when (
      exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      return string.Empty;
    }
  }

  private static bool IsCloaked(nint handle)
  {
    var cloaked = 0;
    var result = DwmGetWindowAttribute(
      handle,
      DwmWindowAttribute.Cloaked,
      out cloaked,
      Marshal.SizeOf<int>());
    return result == 0 && cloaked != 0;
  }

  private delegate bool EnumWindowsProc(nint handle, nint parameter);

  private enum DwmWindowAttribute
  {
    Cloaked = 14
  }

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsWindowVisible(nint handle);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsIconic(nint handle);

  [DllImport("user32.dll")]
  private static extern nint GetShellWindow();

  [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
  private static extern int GetWindowTextLength(nint handle);

  [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
  private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

  [DllImport("dwmapi.dll")]
  private static extern int DwmGetWindowAttribute(
    nint handle,
    DwmWindowAttribute attribute,
    out int value,
    int valueSize);
}
