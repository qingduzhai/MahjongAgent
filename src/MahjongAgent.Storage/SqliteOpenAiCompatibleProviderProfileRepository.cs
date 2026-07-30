using System.Globalization;
using System.Text.Json;
using Dapper;
using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Storage;

public sealed class SqliteOpenAiCompatibleProviderProfileRepository
  : IOpenAiCompatibleProviderProfileRepository
{
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
  private readonly SqliteConnectionFactory connectionFactory;

  public SqliteOpenAiCompatibleProviderProfileRepository(SqliteConnectionFactory connectionFactory)
  {
    ArgumentNullException.ThrowIfNull(connectionFactory);
    this.connectionFactory = connectionFactory;
  }

  public async Task<IReadOnlyList<OpenAiCompatibleProviderProfile>> ListAsync(
    CancellationToken cancellationToken = default)
  {
    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    var rows = await connection.QueryAsync<ProviderProfileRow>(new CommandDefinition(
      $"{SelectColumns} ORDER BY display_name COLLATE NOCASE, id;",
      cancellationToken: cancellationToken)).ConfigureAwait(false);
    return rows.Select(MapProfile).ToArray();
  }

  public async Task<OpenAiCompatibleProviderProfile?> GetAsync(
    string id,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(id);

    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    var row = await connection.QuerySingleOrDefaultAsync<ProviderProfileRow>(new CommandDefinition(
      $"{SelectColumns} WHERE id = @Id;",
      new { Id = id },
      cancellationToken: cancellationToken)).ConfigureAwait(false);
    return row is null ? null : MapProfile(row);
  }

  public async Task UpsertAsync(
    OpenAiCompatibleProviderProfile profile,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(profile);
    profile.Validate();

    var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    var probe = profile.LastProbe;

    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    await connection.ExecuteAsync(new CommandDefinition(
      """
      INSERT INTO openai_compatible_provider_profiles (
        id, display_name, base_uri, allow_insecure_http, chat_completions_path,
        perception_model, credential_id, api_key_transport, api_key_header_name,
        api_key_prefix, capabilities, structured_output_mode, image_detail,
        additional_headers_json, last_probe_at_utc, last_probe_success,
        last_probe_failure_kind, last_verified_capabilities, last_actual_model,
        last_probe_duration_ms, last_provider_request_id, created_at_utc, updated_at_utc)
      VALUES (
        @Id, @DisplayName, @BaseUri, @AllowInsecureHttp, @ChatCompletionsPath,
        @PerceptionModel, @CredentialId, @ApiKeyTransport, @ApiKeyHeaderName,
        @ApiKeyPrefix, @Capabilities, @StructuredOutputMode, @ImageDetail,
        @AdditionalHeadersJson, @LastProbeAtUtc, @LastProbeSuccess,
        @LastProbeFailureKind, @LastVerifiedCapabilities, @LastActualModel,
        @LastProbeDurationMs, @LastProviderRequestId, @Now, @Now)
      ON CONFLICT(id) DO UPDATE SET
        display_name = excluded.display_name,
        base_uri = excluded.base_uri,
        allow_insecure_http = excluded.allow_insecure_http,
        chat_completions_path = excluded.chat_completions_path,
        perception_model = excluded.perception_model,
        credential_id = excluded.credential_id,
        api_key_transport = excluded.api_key_transport,
        api_key_header_name = excluded.api_key_header_name,
        api_key_prefix = excluded.api_key_prefix,
        capabilities = excluded.capabilities,
        structured_output_mode = excluded.structured_output_mode,
        image_detail = excluded.image_detail,
        additional_headers_json = excluded.additional_headers_json,
        last_probe_at_utc = excluded.last_probe_at_utc,
        last_probe_success = excluded.last_probe_success,
        last_probe_failure_kind = excluded.last_probe_failure_kind,
        last_verified_capabilities = excluded.last_verified_capabilities,
        last_actual_model = excluded.last_actual_model,
        last_probe_duration_ms = excluded.last_probe_duration_ms,
        last_provider_request_id = excluded.last_provider_request_id,
        updated_at_utc = excluded.updated_at_utc;
      """,
      new
      {
        profile.Id,
        profile.DisplayName,
        BaseUri = profile.BaseUri.AbsoluteUri,
        AllowInsecureHttp = profile.AllowInsecureHttp ? 1 : 0,
        profile.ChatCompletionsPath,
        profile.PerceptionModel,
        profile.CredentialId,
        ApiKeyTransport = (int)profile.ApiKeyTransport,
        profile.ApiKeyHeaderName,
        profile.ApiKeyPrefix,
        Capabilities = (int)profile.Capabilities,
        StructuredOutputMode = (int)profile.StructuredOutputMode,
        ImageDetail = (int)profile.ImageDetail,
        AdditionalHeadersJson = JsonSerializer.Serialize(profile.AdditionalHeaders, SerializerOptions),
        LastProbeAtUtc = probe?.ProbedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        LastProbeSuccess = probe is null ? (int?)null : probe.IsSuccess ? 1 : 0,
        LastProbeFailureKind = probe is null ? (int?)null : (int)probe.FailureKind,
        LastVerifiedCapabilities = probe is null ? (int?)null : (int)probe.VerifiedCapabilities,
        LastActualModel = probe?.ActualModel,
        LastProbeDurationMs = probe?.DurationMilliseconds,
        LastProviderRequestId = probe?.ProviderRequestId,
        Now = now
      },
      cancellationToken: cancellationToken)).ConfigureAwait(false);
  }

  public async Task<bool> DeleteAsync(
    string id,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(id);

    await using var connection = await connectionFactory
      .OpenConnectionAsync(cancellationToken)
      .ConfigureAwait(false);
    var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
      "DELETE FROM openai_compatible_provider_profiles WHERE id = @Id;",
      new { Id = id },
      cancellationToken: cancellationToken)).ConfigureAwait(false);
    return affectedRows > 0;
  }

  private static OpenAiCompatibleProviderProfile MapProfile(ProviderProfileRow row)
  {
    var additionalHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(
      row.AdditionalHeadersJson,
      SerializerOptions) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    ProviderProbeSnapshot? lastProbe = null;
    if (row.LastProbeAtUtc is not null)
    {
      lastProbe = new ProviderProbeSnapshot
      {
        ProbedAtUtc = DateTimeOffset.Parse(
          row.LastProbeAtUtc,
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind).ToUniversalTime(),
        IsSuccess = row.LastProbeSuccess is 1,
        FailureKind = (ProviderProbeFailureKind)(row.LastProbeFailureKind ?? 0),
        VerifiedCapabilities = (OpenAiCompatibleCapabilities)(row.LastVerifiedCapabilities ?? 0),
        ActualModel = row.LastActualModel,
        DurationMilliseconds = row.LastProbeDurationMs,
        ProviderRequestId = row.LastProviderRequestId
      };
    }

    var profile = new OpenAiCompatibleProviderProfile
    {
      Id = row.Id,
      DisplayName = row.DisplayName,
      BaseUri = new Uri(row.BaseUri, UriKind.Absolute),
      AllowInsecureHttp = row.AllowInsecureHttp is 1,
      ChatCompletionsPath = row.ChatCompletionsPath,
      PerceptionModel = row.PerceptionModel,
      CredentialId = row.CredentialId,
      ApiKeyTransport = (ApiKeyTransport)row.ApiKeyTransport,
      ApiKeyHeaderName = row.ApiKeyHeaderName,
      ApiKeyPrefix = row.ApiKeyPrefix,
      Capabilities = (OpenAiCompatibleCapabilities)row.Capabilities,
      StructuredOutputMode = (StructuredOutputMode)row.StructuredOutputMode,
      ImageDetail = (ImageDetail)row.ImageDetail,
      AdditionalHeaders = new Dictionary<string, string>(
        additionalHeaders,
        StringComparer.OrdinalIgnoreCase),
      LastProbe = lastProbe
    };
    profile.Validate();
    return profile;
  }

  private const string SelectColumns =
    """
    SELECT
      id AS Id,
      display_name AS DisplayName,
      base_uri AS BaseUri,
      allow_insecure_http AS AllowInsecureHttp,
      chat_completions_path AS ChatCompletionsPath,
      perception_model AS PerceptionModel,
      credential_id AS CredentialId,
      api_key_transport AS ApiKeyTransport,
      api_key_header_name AS ApiKeyHeaderName,
      api_key_prefix AS ApiKeyPrefix,
      capabilities AS Capabilities,
      structured_output_mode AS StructuredOutputMode,
      image_detail AS ImageDetail,
      additional_headers_json AS AdditionalHeadersJson,
      last_probe_at_utc AS LastProbeAtUtc,
      last_probe_success AS LastProbeSuccess,
      last_probe_failure_kind AS LastProbeFailureKind,
      last_verified_capabilities AS LastVerifiedCapabilities,
      last_actual_model AS LastActualModel,
      last_probe_duration_ms AS LastProbeDurationMs,
      last_provider_request_id AS LastProviderRequestId
    FROM openai_compatible_provider_profiles
    """;

  private sealed class ProviderProfileRow
  {
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string BaseUri { get; init; }

    public int AllowInsecureHttp { get; init; }

    public required string ChatCompletionsPath { get; init; }

    public required string PerceptionModel { get; init; }

    public string? CredentialId { get; init; }

    public int ApiKeyTransport { get; init; }

    public required string ApiKeyHeaderName { get; init; }

    public required string ApiKeyPrefix { get; init; }

    public int Capabilities { get; init; }

    public int StructuredOutputMode { get; init; }

    public int ImageDetail { get; init; }

    public required string AdditionalHeadersJson { get; init; }

    public string? LastProbeAtUtc { get; init; }

    public int? LastProbeSuccess { get; init; }

    public int? LastProbeFailureKind { get; init; }

    public int? LastVerifiedCapabilities { get; init; }

    public string? LastActualModel { get; init; }

    public long? LastProbeDurationMs { get; init; }

    public string? LastProviderRequestId { get; init; }
  }
}
