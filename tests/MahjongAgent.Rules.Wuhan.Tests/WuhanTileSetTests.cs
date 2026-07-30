using MahjongAgent.Core.Tiles;
using MahjongAgent.Rules.Wuhan;

namespace MahjongAgent.Rules.Wuhan.Tests;

public sealed class WuhanTileSetTests
{
  [Fact]
  public void Wuhan_wall_uses_standard_136_tile_set_without_bonus_tiles()
  {
    Assert.Equal(34, WuhanTileSet.Types.Count);
    Assert.Equal(136, WuhanTileSet.PhysicalTileCount);
    Assert.False(WuhanTileSet.IncludesBonusTiles);
  }

  [Fact]
  public void Red_center_keeps_its_canonical_honor_identity()
  {
    Assert.Equal(TileSuit.Honors, WuhanTileSet.RedCenter.Suit);
    Assert.Equal(HonorTile.RedDragon, WuhanTileSet.RedCenter.Honor);
    Assert.Equal("5z", WuhanTileSet.RedCenter.Code);
  }
}
