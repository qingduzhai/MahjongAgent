namespace MahjongAgent.Capture.Abstractions;

[Flags]
public enum CaptureSourceCapabilities
{
  None = 0,
  Finite = 1 << 0,
  Seekable = 1 << 1,
  RealTime = 1 << 2,
  FastReplay = 1 << 3
}
