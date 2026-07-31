namespace MahjongAgent.Storage;

public class GameEventJournalException : InvalidOperationException
{
  public GameEventJournalException(string message)
    : base(message)
  {
  }

  public GameEventJournalException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}

public sealed class GameEventJournalConflictException : GameEventJournalException
{
  public GameEventJournalConflictException(string message)
    : base(message)
  {
  }

  public GameEventJournalConflictException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}

public sealed class GameEventJournalCorruptionException : GameEventJournalException
{
  public GameEventJournalCorruptionException(string message)
    : base(message)
  {
  }

  public GameEventJournalCorruptionException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
