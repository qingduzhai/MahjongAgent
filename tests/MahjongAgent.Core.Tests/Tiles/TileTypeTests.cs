using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Core.Tests.Tiles;

public sealed class TileTypeTests
{
  [Theory]
  [InlineData("1m", TileSuit.Characters, 1)]
  [InlineData("9m", TileSuit.Characters, 9)]
  [InlineData("1p", TileSuit.Dots, 1)]
  [InlineData("9p", TileSuit.Dots, 9)]
  [InlineData("1s", TileSuit.Bamboo, 1)]
  [InlineData("9s", TileSuit.Bamboo, 9)]
  public void Parse_creates_expected_suited_tile(string code, TileSuit suit, int rank)
  {
    var tile = TileType.Parse(code);

    Assert.Equal(suit, tile.Suit);
    Assert.Equal(rank, tile.Rank);
    Assert.Null(tile.Honor);
    Assert.Equal(code, tile.Code);
  }

  [Theory]
  [InlineData("1z", HonorTile.East)]
  [InlineData("2z", HonorTile.South)]
  [InlineData("3z", HonorTile.West)]
  [InlineData("4z", HonorTile.North)]
  [InlineData("5z", HonorTile.RedDragon)]
  [InlineData("6z", HonorTile.GreenDragon)]
  [InlineData("7z", HonorTile.WhiteDragon)]
  public void Parse_creates_expected_honor_tile(string code, HonorTile honor)
  {
    var tile = TileType.Parse(code);

    Assert.Equal(TileSuit.Honors, tile.Suit);
    Assert.Null(tile.Rank);
    Assert.Equal(honor, tile.Honor);
    Assert.Equal(code, tile.Code);
  }

  [Theory]
  [InlineData("")]
  [InlineData("10m")]
  [InlineData("0m")]
  [InlineData("1x")]
  [InlineData("8z")]
  [InlineData("东")]
  public void Parse_rejects_invalid_code(string code)
  {
    Assert.Throws<FormatException>(() => TileType.Parse(code));
  }

  [Fact]
  public void Canonical_code_is_case_insensitive_on_input()
  {
    var tile = TileType.Parse("5P");

    Assert.Equal("5p", tile.Code);
  }

  [Fact]
  public void Suited_rejects_honor_suit()
  {
    Assert.Throws<ArgumentException>(() => TileType.Suited(TileSuit.Honors, 1));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(10)]
  public void Suited_rejects_rank_outside_one_through_nine(int rank)
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => TileType.Suited(TileSuit.Dots, rank));
  }
}
