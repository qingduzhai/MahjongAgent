namespace MahjongAgent.Core.Gameplay;

/// <summary>
/// A seat relative to the user. This remains stable when a replay does not expose player identities.
/// </summary>
public enum PlayerSeat
{
  Self,
  Next,
  Opposite,
  Previous
}

public static class PlayerSeatExtensions
{
  public static string ToProtocolValue(this PlayerSeat seat) => seat switch
  {
    PlayerSeat.Self => "self",
    PlayerSeat.Next => "next",
    PlayerSeat.Opposite => "opposite",
    PlayerSeat.Previous => "previous",
    _ => throw new ArgumentOutOfRangeException(nameof(seat), seat, "Unsupported player seat.")
  };

  public static bool TryParseProtocolValue(string? value, out PlayerSeat seat)
  {
    seat = value switch
    {
      "self" => PlayerSeat.Self,
      "next" => PlayerSeat.Next,
      "opposite" => PlayerSeat.Opposite,
      "previous" => PlayerSeat.Previous,
      _ => default
    };
    return value is "self" or "next" or "opposite" or "previous";
  }
}
