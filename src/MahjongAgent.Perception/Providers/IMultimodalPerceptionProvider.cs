namespace MahjongAgent.Perception.Providers;

public interface IMultimodalPerceptionProvider
{
  Task<PerceptionResult> ObserveAsync(
    PerceptionRequest request,
    CancellationToken cancellationToken = default);
}
