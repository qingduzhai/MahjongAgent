namespace MahjongAgent.Platform.Windows.Credentials;

internal interface IWindowsCredentialBackend
{
  byte[]? Read(string targetName);

  void Write(string targetName, ReadOnlySpan<byte> secret);

  bool Delete(string targetName);
}
