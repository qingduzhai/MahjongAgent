using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Tests.Tiles;

public sealed class StandardTileSetTests
{
  [Fact]
  public void Standard_set_contains_34_unique_semantic_types()
  {
    Assert.Equal(34, StandardTileSet.Types.Count);
    Assert.Equal(34, StandardTileSet.Types.Distinct().Count());
  }

  [Fact]
  public void Standard_set_contains_expected_family_counts()
  {
    Assert.Equal(9, StandardTileSet.Types.Count(tile => tile.Suit is TileSuit.Characters));
    Assert.Equal(9, StandardTileSet.Types.Count(tile => tile.Suit is TileSuit.Dots));
    Assert.Equal(9, StandardTileSet.Types.Count(tile => tile.Suit is TileSuit.Bamboo));
    Assert.Equal(7, StandardTileSet.Types.Count(tile => tile.Suit is TileSuit.Honors));
  }

  [Fact]
  public void Four_copies_of_each_type_produce_136_physical_tiles()
  {
    Assert.Equal(4, StandardTileSet.CopiesPerType);
    Assert.Equal(136, StandardTileSet.PhysicalTileCount);
  }

  [Fact]
  public void Every_standard_tile_round_trips_through_canonical_code()
  {
    foreach (var tile in StandardTileSet.Types)
    {
      Assert.Equal(tile, TileType.Parse(tile.Code));
    }
  }
}
