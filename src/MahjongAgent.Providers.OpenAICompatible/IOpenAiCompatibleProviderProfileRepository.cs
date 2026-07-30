namespace MahjongAgent.Providers.OpenAICompatible;

public interface IOpenAiCompatibleProviderProfileRepository
{
  Task<IReadOnlyList<OpenAiCompatibleProviderProfile>> ListAsync(
    CancellationToken cancellationToken = default);

  Task<OpenAiCompatibleProviderProfile?> GetAsync(
    string id,
    CancellationToken cancellationToken = default);

  Task UpsertAsync(
    OpenAiCompatibleProviderProfile profile,
    CancellationToken cancellationToken = default);

  Task<bool> DeleteAsync(
    string id,
    CancellationToken cancellationToken = default);
}
