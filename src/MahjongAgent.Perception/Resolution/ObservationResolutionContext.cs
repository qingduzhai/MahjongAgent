using System.Collections.Immutable;
using MahjongAgent.Core.Gameplay;

namespace MahjongAgent.Perception.Resolution;

public sealed record ObservationResolutionContext
{
  public ObservationResolutionContext(
    GameState gameState,
    IEnumerable<KeyValuePair<string, PlayerSeat>> regionSeats,
    IEnumerable<KeyValuePair<string, IndicatorKind>>? regionIndicatorKinds = null,
    IEnumerable<KeyValuePair<string, int>>? confirmationCounts = null)
  {
    ArgumentNullException.ThrowIfNull(gameState);
    ArgumentNullException.ThrowIfNull(regionSeats);
    GameState = gameState;
    RegionSeats = regionSeats.ToImmutableDictionary(StringComparer.Ordinal);
    RegionIndicatorKinds = (regionIndicatorKinds ?? [])
      .ToImmutableDictionary(StringComparer.Ordinal);
    ConfirmationCounts = (confirmationCounts ?? [])
      .ToImmutableDictionary(
        pair => pair.Key,
        pair => pair.Value,
        StringComparer.Ordinal);
    if (ConfirmationCounts.Any(pair => pair.Value < 0))
    {
      throw new ArgumentException("Confirmation counts cannot be negative.", nameof(confirmationCounts));
    }
  }

  public GameState GameState { get; }

  public ImmutableDictionary<string, PlayerSeat> RegionSeats { get; }

  public ImmutableDictionary<string, IndicatorKind> RegionIndicatorKinds { get; }

  public ImmutableDictionary<string, int> ConfirmationCounts { get; }
}
