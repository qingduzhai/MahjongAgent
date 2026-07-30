using System.Collections.ObjectModel;

namespace MahjongAgent.Core.Tiles;

/// <summary>
/// The standard 34 tile types and four physical copies of each type.
/// Bonus/flower tiles are intentionally outside this set.
/// </summary>
public static class StandardTileSet
{
  public const int CopiesPerType = 4;

  private static readonly ReadOnlyCollection<TileType> CanonicalTypes = BuildCanonicalTypes();

  public static IReadOnlyList<TileType> Types => CanonicalTypes;

  public static int PhysicalTileCount => CanonicalTypes.Count * CopiesPerType;

  private static ReadOnlyCollection<TileType> BuildCanonicalTypes()
  {
    var types = new List<TileType>(34);

    foreach (var suit in new[] { TileSuit.Characters, TileSuit.Dots, TileSuit.Bamboo })
    {
      for (var rank = 1; rank <= 9; rank++)
      {
        types.Add(TileType.Suited(suit, rank));
      }
    }

    foreach (var honor in Enum.GetValues<HonorTile>())
    {
      types.Add(TileType.FromHonor(honor));
    }

    return types.AsReadOnly();
  }
}
