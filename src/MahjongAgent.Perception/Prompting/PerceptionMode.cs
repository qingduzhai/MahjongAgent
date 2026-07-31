namespace MahjongAgent.Perception.Prompting;

public enum PerceptionMode
{
  Calibrate,
  Observe,
  Recover
}

public static class PerceptionModeExtensions
{
  public static string ToProtocolValue(this PerceptionMode mode) => mode switch
  {
    PerceptionMode.Calibrate => "calibrate",
    PerceptionMode.Observe => "observe",
    PerceptionMode.Recover => "recover",
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported perception mode.")
  };
}
