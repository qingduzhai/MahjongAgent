using System.Windows;

namespace MahjongAgent.Platform.Windows;

public partial class MainWindow : Window
{
  private readonly MainWindowViewModel viewModel;

  public MainWindow(MainWindowViewModel viewModel)
  {
    ArgumentNullException.ThrowIfNull(viewModel);
    this.viewModel = viewModel;
    DataContext = viewModel;
    InitializeComponent();
    Loaded += MainWindow_Loaded;
  }

  private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
  {
    Loaded -= MainWindow_Loaded;
    await RunUiOperationAsync(() => viewModel.InitializeAsync());
  }

  private void RefreshWindows_Click(object sender, RoutedEventArgs e) =>
    RunUiOperation(viewModel.RefreshWindows);

  private async void Capture_Click(object sender, RoutedEventArgs e) =>
    await RunUiOperationAsync(() => viewModel.CaptureSelectedWindowAsync());

  private async void TestProvider_Click(object sender, RoutedEventArgs e)
  {
    var apiKey = ApiKeyPasswordBox.Password;
    await RunUiOperationAsync(() => viewModel.TestAndSaveProviderAsync(apiKey));
    ApiKeyPasswordBox.Clear();
  }

  private async void Analyze_Click(object sender, RoutedEventArgs e) =>
    await RunUiOperationAsync(() => viewModel.AnalyzeScreenshotAsync());

  private static void RunUiOperation(Action operation)
  {
    try
    {
      operation();
    }
    catch (Exception exception)
    {
      ShowError(exception);
    }
  }

  private static async Task RunUiOperationAsync(Func<Task> operation)
  {
    try
    {
      await operation();
    }
    catch (Exception exception)
    {
      ShowError(exception);
    }
  }

  private static void ShowError(Exception exception) => MessageBox.Show(
    exception.Message,
    "操作失败",
    MessageBoxButton.OK,
    MessageBoxImage.Warning);
}
