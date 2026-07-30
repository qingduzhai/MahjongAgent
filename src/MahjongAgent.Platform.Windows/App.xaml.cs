using System.Windows;
using System.IO;

namespace MahjongAgent.Platform.Windows;

public partial class App : Application
{
  private AppServices? services;

  protected override async void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    ShutdownMode = ShutdownMode.OnExplicitShutdown;
    var smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);

    try
    {
      services = await AppServices.CreateAsync();
      var viewModel = new MainWindowViewModel(services);
      var window = new MainWindow(viewModel);
      MainWindow = window;
      ShutdownMode = ShutdownMode.OnMainWindowClose;
      TryClearStartupError();
      if (smokeTest)
      {
        window.ContentRendered += (_, _) => Dispatcher.BeginInvoke(() => Shutdown(0));
      }

      window.Show();
    }
    catch (Exception exception)
    {
      TryWriteStartupError(exception);
      if (!smokeTest)
      {
        MessageBox.Show(
          $"MahjongAgent 启动失败：\n\n{exception.Message}",
          "启动失败",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }

      Shutdown(1);
    }
  }

  private static void TryWriteStartupError(Exception exception)
  {
    try
    {
      var directory = GetApplicationDataDirectory();
      Directory.CreateDirectory(directory);
      File.WriteAllText(Path.Combine(directory, "startup-error.log"), exception.ToString());
    }
    catch
    {
      // Startup diagnostics must never hide the original failure.
    }
  }

  private static void TryClearStartupError()
  {
    try
    {
      var path = Path.Combine(GetApplicationDataDirectory(), "startup-error.log");
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch
    {
      // A stale diagnostic file must not block startup.
    }
  }

  private static string GetApplicationDataDirectory() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MahjongAgent");

  protected override void OnExit(ExitEventArgs e)
  {
    services?.Dispose();
    base.OnExit(e);
  }
}
