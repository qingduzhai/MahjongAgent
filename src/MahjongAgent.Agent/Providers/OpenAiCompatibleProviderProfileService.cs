using System.Runtime.ExceptionServices;
using MahjongAgent.Providers.OpenAICompatible;

namespace MahjongAgent.Agent.Providers;

public sealed class OpenAiCompatibleProviderProfileService
{
  private readonly IOpenAiCompatibleProviderProfileRepository profileRepository;
  private readonly IApiKeyCredentialStore credentialStore;
  private readonly IOpenAiCompatibleProviderProbeRunner probeRunner;

  public OpenAiCompatibleProviderProfileService(
    IOpenAiCompatibleProviderProfileRepository profileRepository,
    IApiKeyCredentialStore credentialStore,
    IOpenAiCompatibleProviderProbeRunner probeRunner)
  {
    ArgumentNullException.ThrowIfNull(profileRepository);
    ArgumentNullException.ThrowIfNull(credentialStore);
    ArgumentNullException.ThrowIfNull(probeRunner);
    this.profileRepository = profileRepository;
    this.credentialStore = credentialStore;
    this.probeRunner = probeRunner;
  }

  public async Task<ProviderProfileCommitResult> TestAndSaveAsync(
    OpenAiCompatibleProviderProfile profile,
    string? candidateApiKey,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(profile);
    profile.Validate();

    var existingProfile = await profileRepository
      .GetAsync(profile.Id, cancellationToken)
      .ConfigureAwait(false);
    var profiles = await profileRepository
      .ListAsync(cancellationToken)
      .ConfigureAwait(false);
    EnsureCredentialIsNotShared(profile, profiles);

    var probeResult = await probeRunner
      .ProbeAsync(profile, candidateApiKey, cancellationToken)
      .ConfigureAwait(false);
    var testedProfile = profile with
    {
      LastProbe = ProviderProbeSnapshot.FromResult(probeResult, DateTimeOffset.UtcNow)
    };

    if (!probeResult.IsSuccess)
    {
      return new ProviderProfileCommitResult(false, testedProfile, probeResult);
    }

    var newCredentialId = GetCredentialId(testedProfile);
    var oldCredentialId = GetCredentialId(existingProfile);
    var obsoleteCredentialId = oldCredentialId is not null &&
                               !oldCredentialId.Equals(newCredentialId, StringComparison.OrdinalIgnoreCase) &&
                               !IsReferencedByAnotherProfile(oldCredentialId, profile.Id, profiles)
      ? oldCredentialId
      : null;

    string? previousNewCredential = null;
    string? obsoleteCredential = null;
    var candidateCredentialWritten = false;
    var profileWritten = false;
    var obsoleteCredentialDeleted = false;

    if (candidateApiKey is not null)
    {
      previousNewCredential = await credentialStore
        .ResolveApiKeyAsync(newCredentialId!, cancellationToken)
        .ConfigureAwait(false);
    }

    if (obsoleteCredentialId is not null)
    {
      obsoleteCredential = await credentialStore
        .ResolveApiKeyAsync(obsoleteCredentialId, cancellationToken)
        .ConfigureAwait(false);
    }

    try
    {
      if (candidateApiKey is not null)
      {
        await credentialStore
          .SaveApiKeyAsync(newCredentialId!, candidateApiKey, cancellationToken)
          .ConfigureAwait(false);
        candidateCredentialWritten = true;
      }

      await profileRepository
        .UpsertAsync(testedProfile, cancellationToken)
        .ConfigureAwait(false);
      profileWritten = true;

      if (obsoleteCredentialId is not null)
      {
        obsoleteCredentialDeleted = await credentialStore
          .DeleteApiKeyAsync(obsoleteCredentialId, cancellationToken)
          .ConfigureAwait(false);
      }

      return new ProviderProfileCommitResult(true, testedProfile, probeResult);
    }
    catch (Exception operationException)
    {
      var compensationExceptions = await CompensateSaveAsync(
        testedProfile.Id,
        existingProfile,
        newCredentialId,
        previousNewCredential,
        candidateCredentialWritten,
        obsoleteCredentialId,
        obsoleteCredential,
        obsoleteCredentialDeleted,
        profileWritten).ConfigureAwait(false);
      RethrowOperationOrCompensationFailure(operationException, compensationExceptions);
      throw;
    }
  }

  public async Task<bool> DeleteAsync(
    string profileId,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

    var profile = await profileRepository
      .GetAsync(profileId, cancellationToken)
      .ConfigureAwait(false);
    if (profile is null)
    {
      return false;
    }

    var profiles = await profileRepository.ListAsync(cancellationToken).ConfigureAwait(false);
    var credentialId = GetCredentialId(profile);
    var shouldDeleteCredential = credentialId is not null &&
                                 !IsReferencedByAnotherProfile(credentialId, profile.Id, profiles);
    var previousCredential = shouldDeleteCredential
      ? await credentialStore.ResolveApiKeyAsync(credentialId!, cancellationToken).ConfigureAwait(false)
      : null;
    var credentialDeleted = false;

    try
    {
      if (shouldDeleteCredential)
      {
        credentialDeleted = await credentialStore
          .DeleteApiKeyAsync(credentialId!, cancellationToken)
          .ConfigureAwait(false);
      }

      var profileDeleted = await profileRepository
        .DeleteAsync(profileId, cancellationToken)
        .ConfigureAwait(false);
      if (profileDeleted)
      {
        return true;
      }

      if (credentialDeleted && previousCredential is not null)
      {
        await credentialStore
          .SaveApiKeyAsync(credentialId!, previousCredential, CancellationToken.None)
          .ConfigureAwait(false);
      }

      return false;
    }
    catch (Exception operationException)
    {
      var compensationExceptions = new List<Exception>();
      if (credentialDeleted && previousCredential is not null)
      {
        await TryCompensateAsync(
          () => credentialStore.SaveApiKeyAsync(
            credentialId!,
            previousCredential,
            CancellationToken.None).AsTask(),
          compensationExceptions).ConfigureAwait(false);
      }

      RethrowOperationOrCompensationFailure(operationException, compensationExceptions);
      throw;
    }
  }

  private async Task<IReadOnlyList<Exception>> CompensateSaveAsync(
    string profileId,
    OpenAiCompatibleProviderProfile? existingProfile,
    string? newCredentialId,
    string? previousNewCredential,
    bool candidateCredentialWritten,
    string? obsoleteCredentialId,
    string? obsoleteCredential,
    bool obsoleteCredentialDeleted,
    bool profileWritten)
  {
    var compensationExceptions = new List<Exception>();

    if (obsoleteCredentialDeleted && obsoleteCredential is not null)
    {
      await TryCompensateAsync(
        () => credentialStore.SaveApiKeyAsync(
          obsoleteCredentialId!,
          obsoleteCredential,
          CancellationToken.None).AsTask(),
        compensationExceptions).ConfigureAwait(false);
    }

    if (candidateCredentialWritten)
    {
      await TryCompensateAsync(
        previousNewCredential is null
          ? async () =>
          {
            await credentialStore
              .DeleteApiKeyAsync(newCredentialId!, CancellationToken.None)
              .ConfigureAwait(false);
          }
          : () => credentialStore.SaveApiKeyAsync(
            newCredentialId!,
            previousNewCredential,
            CancellationToken.None).AsTask(),
        compensationExceptions).ConfigureAwait(false);
    }

    if (profileWritten)
    {
      await TryCompensateAsync(
        existingProfile is null
          ? async () =>
          {
            await profileRepository.DeleteAsync(profileId, CancellationToken.None).ConfigureAwait(false);
          }
          : () => profileRepository.UpsertAsync(
            existingProfile,
            CancellationToken.None),
        compensationExceptions).ConfigureAwait(false);
    }

    return compensationExceptions;
  }

  private static void EnsureCredentialIsNotShared(
    OpenAiCompatibleProviderProfile profile,
    IReadOnlyList<OpenAiCompatibleProviderProfile> profiles)
  {
    var credentialId = GetCredentialId(profile);
    if (credentialId is null)
    {
      return;
    }

    if (IsReferencedByAnotherProfile(credentialId, profile.Id, profiles))
    {
      throw new InvalidOperationException(
        $"Credential '{credentialId}' is already assigned to another provider profile.");
    }
  }

  private static bool IsReferencedByAnotherProfile(
    string credentialId,
    string profileId,
    IEnumerable<OpenAiCompatibleProviderProfile> profiles) =>
    profiles.Any(candidate =>
      !candidate.Id.Equals(profileId, StringComparison.Ordinal) &&
      GetCredentialId(candidate)?.Equals(credentialId, StringComparison.OrdinalIgnoreCase) is true);

  private static string? GetCredentialId(OpenAiCompatibleProviderProfile? profile) =>
    profile is not null && profile.ApiKeyTransport is not ApiKeyTransport.None
      ? profile.CredentialId
      : null;

  private static async Task TryCompensateAsync(
    Func<Task> compensation,
    ICollection<Exception> exceptions)
  {
    try
    {
      await compensation().ConfigureAwait(false);
    }
    catch (Exception exception)
    {
      exceptions.Add(exception);
    }
  }

  private static void RethrowOperationOrCompensationFailure(
    Exception operationException,
    IReadOnlyList<Exception> compensationExceptions)
  {
    if (compensationExceptions.Count > 0)
    {
      throw new ProviderProfileTransactionException(
        "The provider profile operation failed and one or more compensation actions also failed.",
        operationException,
        compensationExceptions);
    }

    ExceptionDispatchInfo.Capture(operationException).Throw();
  }
}
