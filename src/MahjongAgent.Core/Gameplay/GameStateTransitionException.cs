namespace MahjongAgent.Core.Gameplay;

public sealed class GameStateTransitionException(string message) : InvalidOperationException(message);
