namespace MahjongAgent.Capture.Abstractions;

public sealed record CaptureReadRequest
{
  public TimeSpan? StartPosition { get; init; }

  public TimeSpan? EndPosition { get; init; }

  public TimeSpan MinimumFrameInterval { get; init; } = TimeSpan.Zero;

  public CapturePacingMode PacingMode { get; init; } = CapturePacingMode.RealTime;

  public void ValidateFor(CaptureSourceDescriptor source)
  {
    ArgumentNullException.ThrowIfNull(source);

    if (!Enum.IsDefined(PacingMode))
    {
      throw new ArgumentOutOfRangeException(nameof(PacingMode));
    }

    if (StartPosition is { } start && start < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(StartPosition));
    }

    if (EndPosition is { } end && end < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(EndPosition));
    }

    if (StartPosition is { } startPosition &&
        EndPosition is { } endPosition &&
        endPosition <= startPosition)
    {
      throw new ArgumentException("EndPosition must be greater than StartPosition.");
    }

    if (MinimumFrameInterval < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(MinimumFrameInterval));
    }

    var capabilities = source.Capabilities;
    if ((StartPosition is not null || EndPosition is not null) &&
        !capabilities.HasFlag(CaptureSourceCapabilities.Seekable))
    {
      throw new NotSupportedException("The capture source does not support timeline ranges.");
    }

    var requiredCapability = PacingMode switch
    {
      CapturePacingMode.RealTime => CaptureSourceCapabilities.RealTime,
      CapturePacingMode.AsFastAsPossible => CaptureSourceCapabilities.FastReplay,
      _ => CaptureSourceCapabilities.None
    };

    if (!capabilities.HasFlag(requiredCapability))
    {
      throw new NotSupportedException($"The capture source does not support {PacingMode} pacing.");
    }
  }
}
