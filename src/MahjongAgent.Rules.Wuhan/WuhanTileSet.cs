using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Rules.Wuhan;

/// <summary>
/// Wuhan Mahjong uses the standard 34 semantic tile types, four copies each,
/// and excludes bonus/flower tiles from the 136-tile wall.
/// </summary>
public static class WuhanTileSet
{
  public static IReadOnlyList<TileType> Types => StandardTileSet.Types;

  public static int PhysicalTileCount => StandardTileSet.PhysicalTileCount;

  public static bool IncludesBonusTiles => false;

  public static TileType RedCenter => TileType.FromHonor(HonorTile.RedDragon);
}
