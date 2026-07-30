namespace MahjongAgent.Core.Tiles;

/// <summary>
/// The platform-independent semantic identity of a Mahjong tile.
/// Visual appearance, location, orientation and round-specific roles belong to other models.
/// </summary>
public sealed record TileType
{
  private TileType(TileSuit suit, int? rank, HonorTile? honor)
  {
    Suit = suit;
    Rank = rank;
    Honor = honor;
  }

  public TileSuit Suit { get; }

  public int? Rank { get; }

  public HonorTile? Honor { get; }

  public bool IsSuited => Suit is not TileSuit.Honors;

  public string Code => Suit switch
  {
    TileSuit.Characters => $"{Rank}m",
    TileSuit.Dots => $"{Rank}p",
    TileSuit.Bamboo => $"{Rank}s",
    TileSuit.Honors => $"{(int)Honor!.Value}z",
    _ => throw new InvalidOperationException($"Unsupported tile suit: {Suit}.")
  };

  public static TileType Suited(TileSuit suit, int rank)
  {
    if (suit is TileSuit.Honors)
    {
      throw new ArgumentException("Honor tiles must be created with FromHonor.", nameof(suit));
    }

    if (rank is < 1 or > 9)
    {
      throw new ArgumentOutOfRangeException(nameof(rank), rank, "A suited tile rank must be from 1 through 9.");
    }

    return new TileType(suit, rank, null);
  }

  public static TileType FromHonor(HonorTile honor)
  {
    if (!Enum.IsDefined(honor))
    {
      throw new ArgumentOutOfRangeException(nameof(honor), honor, "The honor value must be one of z1 through z7.");
    }

    return new TileType(TileSuit.Honors, null, honor);
  }

  public static TileType Parse(string code)
  {
    if (!TryParse(code, out var tile))
    {
      throw new FormatException($"'{code}' is not a canonical Mahjong tile code.");
    }

    return tile;
  }

  public static bool TryParse(string? code, out TileType tile)
  {
    tile = null!;

    if (code is null || code.Length != 2 || code[0] is < '1' or > '9')
    {
      return false;
    }

    var value = code[0] - '0';
    switch (char.ToLowerInvariant(code[1]))
    {
      case 'm':
        tile = Suited(TileSuit.Characters, value);
        return true;
      case 'p':
        tile = Suited(TileSuit.Dots, value);
        return true;
      case 's':
        tile = Suited(TileSuit.Bamboo, value);
        return true;
      case 'z' when value <= 7:
        tile = FromHonor((HonorTile)value);
        return true;
      default:
        return false;
    }
  }

  public override string ToString() => Code;
}
