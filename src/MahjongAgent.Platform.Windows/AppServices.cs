using MahjongAgent.Agent.Orchestration;
using MahjongAgent.Agent.Providers;
using MahjongAgent.Platform.Windows.Credentials;
using MahjongAgent.Platform.Windows.WindowCapture;
using MahjongAgent.Providers.OpenAICompatible;
using MahjongAgent.Storage;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Net.Http;

namespace MahjongAgent.Platform.Windows;

internal sealed class AppServices : IDisposable
{
  private AppServices(
    HttpClient httpClient,
    IApiKeyCredentialStore credentialStore,
    IOpenAiCompatibleProviderProfileRepository profileRepository,
    OpenAiCompatibleProviderProfileService providerProfileService,
    AgentOrchestrator agentOrchestrator)
  {
    HttpClient = httpClient;
    CredentialStore = credentialStore;
    ProfileRepository = profileRepository;
    ProviderProfileService = providerProfileService;
    AgentOrchestrator = agentOrchestrator;
  }

  public HttpClient HttpClient { get; }

  public IApiKeyCredentialStore CredentialStore { get; }

  public IOpenAiCompatibleProviderProfileRepository ProfileRepository { get; }

  public OpenAiCompatibleProviderProfileService ProviderProfileService { get; }

  public AgentOrchestrator AgentOrchestrator { get; }

  public WindowCatalog WindowCatalog { get; } = new();

  public WindowScreenshotCapture ScreenshotCapture { get; } = new();

  public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken = default)
  {
    var applicationDataDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "MahjongAgent");
    Directory.CreateDirectory(applicationDataDirectory);

    var databasePath = Path.Combine(applicationDataDirectory, "mahjong-agent.db");
    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = databasePath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Cache = SqliteCacheMode.Shared
    }.ToString();
    var connectionFactory = new SqliteConnectionFactory(connectionString);
    await new SqliteMigrationRunner(connectionFactory)
      .ApplyAsync(cancellationToken)
      .ConfigureAwait(false);

    var credentialStore = new WindowsCredentialManagerApiKeyStore();
    var profileRepository = new SqliteOpenAiCompatibleProviderProfileRepository(connectionFactory);
    var httpClient = new HttpClient();
    var probeRunner = new OpenAiCompatibleProviderProbeRunner(httpClient, credentialStore);
    var providerProfileService = new OpenAiCompatibleProviderProfileService(
      profileRepository,
      credentialStore,
      probeRunner);
    var agentOrchestrator = await AgentOrchestrator
      .CreateAsync(cancellationToken: cancellationToken)
      .ConfigureAwait(false);

    return new AppServices(
      httpClient,
      credentialStore,
      profileRepository,
      providerProfileService,
      agentOrchestrator);
  }

  public void Dispose() => HttpClient.Dispose();
}
