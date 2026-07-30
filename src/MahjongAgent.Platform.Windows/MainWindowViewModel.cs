using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using MahjongAgent.Agent.Orchestration;
using MahjongAgent.Perception.Providers;
using MahjongAgent.Platform.Windows.WindowCapture;
using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Platform.Windows;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
  private const string DefaultProfileId = "default";
  private const string DefaultCredentialId = "provider/default";
  private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };
  private readonly AppServices services;
  private WindowCandidate? selectedWindow;
  private WindowScreenshot? screenshot;
  private ImageSource? screenshotPreview;
  private string captureStatus = "请选择一个包含麻将牌局画面的窗口。";
  private string providerStatus = "尚未验证多模态服务。";
  private string analysisJson = "尚未分析截图。";
  private string baseUrl = "https://api.openai.com/v1/";
  private string completionsPath = "chat/completions";
  private string modelId = string.Empty;
  private ApiKeyTransport authentication = ApiKeyTransport.Bearer;
  private string apiKeyHeaderName = "api-key";
  private string apiKeyPrefix = string.Empty;
  private StructuredOutputMode structuredOutput = StructuredOutputMode.JsonSchema;
  private ImageDetail imageDetail = ImageDetail.High;
  private bool isBusy;
  private AgentPhase agentPhase;

  internal MainWindowViewModel(AppServices services)
  {
    ArgumentNullException.ThrowIfNull(services);
    this.services = services;
    agentPhase = services.AgentOrchestrator.Current.Phase;
  }

  public event PropertyChangedEventHandler? PropertyChanged;

  public ObservableCollection<WindowCandidate> Windows { get; } = [];

  public IReadOnlyList<ApiKeyTransport> AuthenticationModes { get; } =
    [ApiKeyTransport.Bearer, ApiKeyTransport.Header, ApiKeyTransport.None];

  public IReadOnlyList<StructuredOutputMode> StructuredOutputModes { get; } =
    [StructuredOutputMode.JsonSchema, StructuredOutputMode.JsonObject];

  public IReadOnlyList<ImageDetail> ImageDetails { get; } =
    [ImageDetail.Auto, ImageDetail.Low, ImageDetail.High];

  public WindowCandidate? SelectedWindow
  {
    get => selectedWindow;
    set => SetProperty(ref selectedWindow, value);
  }

  public ImageSource? ScreenshotPreview
  {
    get => screenshotPreview;
    private set => SetProperty(ref screenshotPreview, value);
  }

  public string CaptureStatus
  {
    get => captureStatus;
    private set => SetProperty(ref captureStatus, value);
  }

  public string ProviderStatus
  {
    get => providerStatus;
    private set => SetProperty(ref providerStatus, value);
  }

  public string AnalysisJson
  {
    get => analysisJson;
    private set => SetProperty(ref analysisJson, value);
  }

  public string BaseUrl
  {
    get => baseUrl;
    set => SetProperty(ref baseUrl, value);
  }

  public string CompletionsPath
  {
    get => completionsPath;
    set => SetProperty(ref completionsPath, value);
  }

  public string ModelId
  {
    get => modelId;
    set => SetProperty(ref modelId, value);
  }

  public ApiKeyTransport Authentication
  {
    get => authentication;
    set => SetProperty(ref authentication, value);
  }

  public string ApiKeyHeaderName
  {
    get => apiKeyHeaderName;
    set => SetProperty(ref apiKeyHeaderName, value);
  }

  public string ApiKeyPrefix
  {
    get => apiKeyPrefix;
    set => SetProperty(ref apiKeyPrefix, value);
  }

  public StructuredOutputMode StructuredOutput
  {
    get => structuredOutput;
    set => SetProperty(ref structuredOutput, value);
  }

  public ImageDetail ImageDetail
  {
    get => imageDetail;
    set => SetProperty(ref imageDetail, value);
  }

  public bool IsBusy
  {
    get => isBusy;
    private set => SetProperty(ref isBusy, value);
  }

  public AgentPhase AgentPhase
  {
    get => agentPhase;
    private set => SetProperty(ref agentPhase, value);
  }

  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    RefreshWindows();
    var existing = await services.ProfileRepository
      .GetAsync(DefaultProfileId, cancellationToken);
    if (existing is null)
    {
      return;
    }

    BaseUrl = existing.BaseUri.AbsoluteUri;
    CompletionsPath = existing.ChatCompletionsPath;
    ModelId = existing.PerceptionModel;
    Authentication = existing.ApiKeyTransport;
    ApiKeyHeaderName = existing.ApiKeyHeaderName;
    ApiKeyPrefix = existing.ApiKeyPrefix;
    StructuredOutput = existing.StructuredOutputMode;
    ImageDetail = existing.ImageDetail;
    ProviderStatus = existing.LastProbe is { IsSuccess: true } probe
      ? $"上次验证成功 · {probe.ActualModel ?? existing.PerceptionModel} · {probe.ProbedAtUtc.ToLocalTime():g}"
      : "已加载 Provider 配置，请重新测试连接。";
  }

  public void RefreshWindows()
  {
    var previousHandle = SelectedWindow?.Handle;
    Windows.Clear();
    foreach (var window in services.WindowCatalog.ListVisibleWindows())
    {
      Windows.Add(window);
    }

    SelectedWindow = Windows.FirstOrDefault(window => window.Handle == previousHandle) ??
      Windows.FirstOrDefault();
    CaptureStatus = Windows.Count == 0
      ? "没有发现可捕获的普通窗口。"
      : $"已发现 {Windows.Count} 个窗口。";
  }

  public async Task CaptureSelectedWindowAsync(CancellationToken cancellationToken = default)
  {
    var selected = SelectedWindow ?? throw new InvalidOperationException("请先选择目标窗口。");
    CaptureStatus = "正在截取目标窗口……";
    await RunBusyAsync(async () =>
    {
      screenshot = await Task
        .Run(() => services.ScreenshotCapture.Capture(selected.Handle), cancellationToken)
        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
      ScreenshotPreview = screenshot.Preview;
      CaptureStatus = $"截图成功 · {screenshot.PixelWidth}×{screenshot.PixelHeight} · " +
                      $"{screenshot.CapturedAtUtc.ToLocalTime():T}";
    });
  }

  public async Task TestAndSaveProviderAsync(
    string? candidateApiKey,
    CancellationToken cancellationToken = default)
  {
    await RunBusyAsync(async () =>
    {
      var profile = BuildProfile();
      ProviderStatus = "正在验证鉴权、图片输入和结构化输出……";
      var result = await services.ProviderProfileService.TestAndSaveAsync(
        profile,
        NormalizeCandidateApiKey(candidateApiKey),
        cancellationToken);
      ProviderStatus = result.IsSaved
        ? $"验证成功并已安全保存 · 模型 {result.ProbeResult.Model} · " +
          $"{result.ProbeResult.Duration.TotalMilliseconds:F0} ms"
        : $"验证失败 [{result.ProbeResult.FailureKind}] · {result.ProbeResult.Message}";
    });
  }

  public async Task AnalyzeScreenshotAsync(CancellationToken cancellationToken = default)
  {
    if (screenshot is null)
    {
      throw new InvalidOperationException("请先截取目标窗口画面。");
    }

    await RunBusyAsync(async () =>
    {
      var profile = await services.ProfileRepository.GetAsync(DefaultProfileId, cancellationToken) ??
        throw new InvalidOperationException("请先测试并保存多模态 Provider。");
      if (profile.LastProbe is not { IsSuccess: true })
      {
        throw new InvalidOperationException("Provider 尚未通过能力验证。");
      }

      AnalysisJson = "正在识别当前截图……";
      var provider = new OpenAiCompatiblePerceptionProvider(
        services.HttpClient,
        services.CredentialStore,
        profile.ToOptions());
      var request = new PerceptionRequest(
        "你是麻将桌面观察器。只描述截图中清晰可见的内容，不猜测被遮挡或未显示的信息。",
        "判断这是否是麻将牌局画面，并概括当前可见区域。若能清楚识别当前玩家手牌，则按从左到右列出牌面；不能确认的牌写 unknown。",
        [new PerceptionImage("image/png", screenshot.PngBytes)],
        CreatePreviewSchema(),
        $"preview-{Guid.NewGuid():N}");
      var result = await provider.ObserveAsync(request, cancellationToken);
      AnalysisJson = JsonSerializer.Serialize(result.StructuredOutput, PrettyJson);
    });
  }

  private OpenAiCompatibleProviderProfile BuildProfile()
  {
    if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var baseUri))
    {
      throw new ArgumentException("Base URL 必须是绝对地址。");
    }

    if (!baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
    {
      throw new ArgumentException("Base URL 必须以 / 结尾，例如 https://api.openai.com/v1/。");
    }

    var path = CompletionsPath.Trim();
    if (path.StartsWith('/'))
    {
      throw new ArgumentException("Chat Completions Path 是相对路径，不能以 / 开头。");
    }

    var capabilities = OpenAiCompatibleCapabilities.Vision |
      (StructuredOutput is StructuredOutputMode.JsonSchema
        ? OpenAiCompatibleCapabilities.JsonSchema
        : OpenAiCompatibleCapabilities.JsonObject);

    return new OpenAiCompatibleProviderProfile
    {
      Id = DefaultProfileId,
      DisplayName = "Default Provider",
      BaseUri = baseUri,
      ChatCompletionsPath = path,
      PerceptionModel = ModelId.Trim(),
      CredentialId = Authentication is ApiKeyTransport.None ? null : DefaultCredentialId,
      ApiKeyTransport = Authentication,
      ApiKeyHeaderName = ApiKeyHeaderName.Trim(),
      ApiKeyPrefix = ApiKeyPrefix,
      Capabilities = capabilities,
      StructuredOutputMode = StructuredOutput,
      ImageDetail = ImageDetail
    };
  }

  private string? NormalizeCandidateApiKey(string? candidateApiKey) =>
    Authentication is ApiKeyTransport.None || string.IsNullOrWhiteSpace(candidateApiKey)
      ? null
      : candidateApiKey;

  private async Task RunBusyAsync(Func<Task> operation)
  {
    if (IsBusy)
    {
      return;
    }

    IsBusy = true;
    try
    {
      await operation();
    }
    finally
    {
      IsBusy = false;
      AgentPhase = services.AgentOrchestrator.Current.Phase;
    }
  }

  private static StructuredOutputSchema CreatePreviewSchema()
  {
    using var document = JsonDocument.Parse(
      """
      {
        "type": "object",
        "additionalProperties": false,
        "required": ["is_mahjong", "summary", "visible_self_tiles", "uncertainties"],
        "properties": {
          "is_mahjong": { "type": "boolean" },
          "summary": { "type": "string" },
          "visible_self_tiles": {
            "type": "array",
            "items": { "type": "string" }
          },
          "uncertainties": {
            "type": "array",
            "items": { "type": "string" }
          }
        }
      }
      """);
    return new StructuredOutputSchema("mahjong_screenshot_preview", document.RootElement);
  }

  private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
  {
    if (EqualityComparer<T>.Default.Equals(field, value))
    {
      return false;
    }

    field = value;
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    return true;
  }
}
