namespace MahjongAgent.Agent.Orchestration;

public static class AgentStateMachine
{
  public static AgentTransition Apply(AgentWorkflowSnapshot snapshot, AgentSignal signal)
  {
    ArgumentNullException.ThrowIfNull(snapshot);
    ArgumentNullException.ThrowIfNull(signal);
    ValidateSnapshot(snapshot);

    if (signal.OccurredAtUtc.Offset != TimeSpan.Zero)
    {
      throw new ArgumentException("Signal timestamps must use the UTC offset.", nameof(signal));
    }

    var next = ApplyCore(snapshot, signal) with
    {
      Revision = checked(snapshot.Revision + 1),
      UpdatedAtUtc = signal.OccurredAtUtc
    };
    ValidateSnapshot(next);

    return new AgentTransition(snapshot, next, signal);
  }

  private static AgentWorkflowSnapshot ApplyCore(
    AgentWorkflowSnapshot snapshot,
    AgentSignal signal) => signal switch
  {
    StopRequestedSignal when snapshot.Phase is not AgentPhase.Stopped => snapshot with
    {
      Phase = AgentPhase.Stopped,
      ResumePhase = null,
      LastFailure = null
    },
    ResetRequestedSignal when snapshot.Phase is AgentPhase.Faulted or AgentPhase.Stopped =>
      AgentWorkflowSnapshot.Initial,
    FatalFailureSignal failure when snapshot.Phase is not AgentPhase.Stopped => snapshot with
    {
      Phase = AgentPhase.Faulted,
      ResumePhase = null,
      LastFailure = CreateFailure(failure.Code, failure.Message, false)
    },
    PauseRequestedSignal when CanPause(snapshot.Phase) => snapshot with
    {
      Phase = AgentPhase.Paused,
      ResumePhase = snapshot.Phase
    },
    ResumeRequestedSignal when snapshot.Phase is AgentPhase.Paused && snapshot.ResumePhase is not null =>
      snapshot with
      {
        Phase = snapshot.ResumePhase.Value,
        ResumePhase = null
      },
    RecoverableFailureSignal failure when CanRecover(snapshot.Phase) => snapshot with
    {
      Phase = AgentPhase.Recovering,
      ResumePhase = snapshot.Phase,
      LastFailure = CreateFailure(failure.Code, failure.Message, true)
    },
    RecoveryCompletedSignal when snapshot.Phase is AgentPhase.Recovering && snapshot.ResumePhase is not null =>
      snapshot with
      {
        Phase = snapshot.ResumePhase.Value,
        ResumePhase = null,
        LastFailure = null
      },
    TargetAttachedSignal attached when snapshot.Phase is AgentPhase.Idle => Attach(snapshot, attached),
    CalibrationCompletedSignal when snapshot.Phase is AgentPhase.Calibrating => snapshot with
    {
      Phase = AgentPhase.WaitingForRound
    },
    RoundStartedSignal when snapshot.Phase is AgentPhase.WaitingForRound => snapshot with
    {
      Phase = AgentPhase.Observing
    },
    FrameChangedSignal frame when snapshot.Phase is AgentPhase.Observing => Observe(snapshot, frame),
    ObservationResolvedSignal resolved when snapshot.Phase is AgentPhase.ResolvingObservation =>
      ResolveObservation(snapshot, resolved),
    StateUpdatedSignal updated when snapshot.Phase is AgentPhase.UpdatingState => snapshot with
    {
      Phase = updated.RequiresDecision ? AgentPhase.EvaluatingStrategy : AgentPhase.Observing
    },
    StrategyEvaluatedSignal evaluated when snapshot.Phase is AgentPhase.EvaluatingStrategy => snapshot with
    {
      Phase = evaluated.ShouldPublish ? AgentPhase.PublishingAdvice : AgentPhase.Observing
    },
    AdvicePublishedSignal when snapshot.Phase is AgentPhase.PublishingAdvice => snapshot with
    {
      Phase = AgentPhase.Observing
    },
    RoundEndedSignal when snapshot.Phase is AgentPhase.Observing => snapshot with
    {
      Phase = AgentPhase.ConsolidatingMemory
    },
    MemoryConsolidatedSignal when snapshot.Phase is AgentPhase.ConsolidatingMemory => snapshot with
    {
      Phase = AgentPhase.WaitingForRound
    },
    _ => throw new AgentTransitionException(snapshot.Phase, signal.GetType())
  };

  private static AgentWorkflowSnapshot Attach(
    AgentWorkflowSnapshot snapshot,
    TargetAttachedSignal signal)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(signal.SourceId);
    ArgumentException.ThrowIfNullOrWhiteSpace(signal.SessionId);

    return snapshot with
    {
      Phase = signal.RequiresCalibration ? AgentPhase.Calibrating : AgentPhase.WaitingForRound,
      SourceId = signal.SourceId,
      SessionId = signal.SessionId,
      LastFrameSequenceNumber = null,
      ResumePhase = null,
      LastFailure = null
    };
  }

  private static AgentWorkflowSnapshot Observe(
    AgentWorkflowSnapshot snapshot,
    FrameChangedSignal signal)
  {
    if (signal.SequenceNumber < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(signal));
    }

    return snapshot with
    {
      Phase = AgentPhase.ResolvingObservation,
      LastFrameSequenceNumber = signal.SequenceNumber
    };
  }

  private static AgentWorkflowSnapshot ResolveObservation(
    AgentWorkflowSnapshot snapshot,
    ObservationResolvedSignal signal)
  {
    if (signal.AcceptedEventCount < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(signal));
    }

    return snapshot with
    {
      Phase = signal.AcceptedEventCount > 0 ? AgentPhase.UpdatingState : AgentPhase.Observing
    };
  }

  private static AgentFailure CreateFailure(string code, string message, bool isRecoverable) =>
    new AgentFailure(code, message, isRecoverable).Validate();

  private static bool CanPause(AgentPhase phase) => phase is
    AgentPhase.Calibrating or
    AgentPhase.WaitingForRound or
    AgentPhase.Observing or
    AgentPhase.ResolvingObservation or
    AgentPhase.UpdatingState or
    AgentPhase.EvaluatingStrategy or
    AgentPhase.PublishingAdvice;

  private static bool CanRecover(AgentPhase phase) => phase is
    AgentPhase.Calibrating or
    AgentPhase.WaitingForRound or
    AgentPhase.Observing or
    AgentPhase.ResolvingObservation or
    AgentPhase.UpdatingState or
    AgentPhase.EvaluatingStrategy or
    AgentPhase.PublishingAdvice or
    AgentPhase.ConsolidatingMemory;

  internal static void ValidateSnapshot(AgentWorkflowSnapshot snapshot)
  {
    if (snapshot.Revision < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(snapshot));
    }

    if (!Enum.IsDefined(snapshot.Phase))
    {
      throw new ArgumentOutOfRangeException(nameof(snapshot));
    }

    if (snapshot.UpdatedAtUtc.Offset != TimeSpan.Zero)
    {
      throw new ArgumentException("Snapshot timestamps must use the UTC offset.", nameof(snapshot));
    }

    if (snapshot.LastFrameSequenceNumber < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(snapshot));
    }

    if (snapshot.Phase is not (AgentPhase.Idle or AgentPhase.Stopped or AgentPhase.Faulted) &&
        (string.IsNullOrWhiteSpace(snapshot.SourceId) ||
         string.IsNullOrWhiteSpace(snapshot.SessionId)))
    {
      throw new ArgumentException(
        "A non-idle agent snapshot must identify its source and session.",
        nameof(snapshot));
    }

    var needsResumePhase = snapshot.Phase is AgentPhase.Paused or AgentPhase.Recovering;
    if (needsResumePhase != (snapshot.ResumePhase is not null))
    {
      throw new ArgumentException(
        "ResumePhase must be present only while the agent is paused or recovering.",
        nameof(snapshot));
    }

    var needsFailure = snapshot.Phase is AgentPhase.Recovering or AgentPhase.Faulted;
    if (needsFailure != (snapshot.LastFailure is not null))
    {
      throw new ArgumentException(
        "LastFailure must be present only while the agent is recovering or faulted.",
        nameof(snapshot));
    }
  }
}
