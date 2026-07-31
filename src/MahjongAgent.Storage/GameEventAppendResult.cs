using MahjongAgent.Core.Gameplay;

namespace MahjongAgent.Storage;

public enum GameEventAppendStatus
{
  Appended,
  AlreadyPresent
}

public sealed record GameEventAppendResult(
  GameEventAppendStatus Status,
  GameState GameState);
