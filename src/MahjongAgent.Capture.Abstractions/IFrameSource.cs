namespace MahjongAgent.Capture.Abstractions;

public interface IFrameSource : IAsyncDisposable
{
  // Every yielded frame is one independent still image. Implementations may
  // capture a live window or extract screenshots from recorded material.
  CaptureSourceDescriptor Descriptor { get; }

  IAsyncEnumerable<CaptureFrame> ReadFramesAsync(
    CaptureReadRequest request,
    CancellationToken cancellationToken = default);
}
