using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;

namespace MahjongAgent.Platform.Windows.Credentials;

internal sealed class WindowsCredentialManagerBackend : IWindowsCredentialBackend
{
  private const uint CredentialTypeGeneric = 1;
  private const uint CredentialPersistLocalMachine = 2;
  private const int ErrorNotFound = 1168;

  public byte[]? Read(string targetName)
  {
    if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
    {
      var error = Marshal.GetLastWin32Error();
      if (error is ErrorNotFound)
      {
        return null;
      }

      throw new Win32Exception(error, "Windows Credential Manager could not read the credential.");
    }

    try
    {
      var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
      if (credential.CredentialBlobSize is 0 || credential.CredentialBlob == IntPtr.Zero)
      {
        return [];
      }

      var secret = new byte[checked((int)credential.CredentialBlobSize)];
      Marshal.Copy(credential.CredentialBlob, secret, 0, secret.Length);
      return secret;
    }
    finally
    {
      CredFree(credentialPointer);
    }
  }

  public void Write(string targetName, ReadOnlySpan<byte> secret)
  {
    var secretCopy = secret.ToArray();
    var secretPointer = Marshal.AllocCoTaskMem(secretCopy.Length);
    try
    {
      Marshal.Copy(secretCopy, 0, secretPointer, secretCopy.Length);
      var credential = new NativeCredential
      {
        Type = CredentialTypeGeneric,
        TargetName = targetName,
        CredentialBlobSize = checked((uint)secretCopy.Length),
        CredentialBlob = secretPointer,
        Persist = CredentialPersistLocalMachine,
        UserName = "MahjongAgent"
      };

      if (!CredWrite(ref credential, 0))
      {
        var error = Marshal.GetLastWin32Error();
        throw new Win32Exception(error, "Windows Credential Manager could not save the credential.");
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(secretCopy);
      for (var index = 0; index < secret.Length; index++)
      {
        Marshal.WriteByte(secretPointer, index, 0);
      }

      Marshal.FreeCoTaskMem(secretPointer);
    }
  }

  public bool Delete(string targetName)
  {
    if (CredDelete(targetName, CredentialTypeGeneric, 0))
    {
      return true;
    }

    var error = Marshal.GetLastWin32Error();
    if (error is ErrorNotFound)
    {
      return false;
    }

    throw new Win32Exception(error, "Windows Credential Manager could not delete the credential.");
  }

  [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool CredRead(
    string target,
    uint type,
    uint flags,
    out IntPtr credentialPointer);

  [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool CredWrite(ref NativeCredential credential, uint flags);

  [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool CredDelete(string target, uint type, uint flags);

  [DllImport("Advapi32.dll")]
  private static extern void CredFree(IntPtr buffer);

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct NativeCredential
  {
    public uint Flags;

    public uint Type;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string TargetName;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string? Comment;

    public FILETIME LastWritten;

    public uint CredentialBlobSize;

    public IntPtr CredentialBlob;

    public uint Persist;

    public uint AttributeCount;

    public IntPtr Attributes;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string? TargetAlias;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string UserName;
  }
}
