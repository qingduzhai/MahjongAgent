namespace MahjongAgent.Platform.Windows.WindowCapture;

public sealed record WindowCandidate(
  nint Handle,
  string Title,
  string ProcessName)
{
  public string DisplayLabel => string.IsNullOrWhiteSpace(ProcessName)
    ? Title
    : $"{Title}  ·  {ProcessName}";
}
