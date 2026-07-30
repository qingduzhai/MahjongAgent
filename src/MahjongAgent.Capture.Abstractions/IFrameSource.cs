namespace MahjongAgent.Capture.Abstractions;

public interface IFrameSource : IAsyncDisposable
{
  CaptureSourceDescriptor Descriptor { get; }

  IAsyncEnumerable<CaptureFrame> ReadFramesAsync(
    CaptureReadRequest request,
    CancellationToken cancellationToken = default);
}
