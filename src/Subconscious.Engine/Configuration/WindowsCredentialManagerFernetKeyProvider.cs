using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Subconscious.Engine.Configuration;

/// <summary>Obtains the Fernet key Python keyring stores in Windows Credential Manager.</summary>
public interface IFernetKeyProvider
{
    string GetOrCreateKey();
}

public sealed class WindowsCredentialManagerFernetKeyProvider : IFernetKeyProvider
{
    private const string ServiceName = "subconscious";
    private const string UserName = "encryption_key";
    private readonly object _sync = new();
    private string? _key;

    public string GetOrCreateKey()
    {
        lock (_sync)
        {
            if (_key is not null)
            {
                return _key;
            }

            var configuredKey = Environment.GetEnvironmentVariable("SUBCONSCIOUS_FERNET_KEY");
            if (!string.IsNullOrWhiteSpace(configuredKey))
            {
                _key = configuredKey.Trim();
                return _key;
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new ModelConfigurationStoreException(
                    "Reading Python data.enc requires SUBCONSCIOUS_FERNET_KEY outside Windows.");
            }

            _key = WindowsCredentialManager.Read(ServiceName, UserName) ?? CreateAndPersistKey();
            return _key;
        }
    }

    private static string CreateAndPersistKey()
    {
        var rawKey = RandomNumberGenerator.GetBytes(32);
        var key = Convert.ToBase64String(rawKey).Replace('+', '-').Replace('/', '_');
        WindowsCredentialManager.Write(ServiceName, UserName, key);
        return key;
    }
}

internal static class WindowsCredentialManager
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string? Read(string targetName, string expectedUserName)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }
            throw new ModelConfigurationStoreException($"Could not read the '{targetName}' Windows credential (error {error}).");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            var userName = Marshal.PtrToStringUni(credential.UserName);
            if (!string.Equals(userName, expectedUserName, StringComparison.Ordinal))
            {
                throw new ModelConfigurationStoreException($"The '{targetName}' Windows credential is not the Subconscious encryption credential.");
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return DecodeCredentialBlob(blob);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static void Write(string targetName, string userName, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var targetPointer = Marshal.StringToCoTaskMemUni(targetName);
        var userPointer = Marshal.StringToCoTaskMemUni(userName);
        var blobPointer = Marshal.AllocCoTaskMem(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetPointer,
                UserName = userPointer,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new ModelConfigurationStoreException(
                    $"Could not save the Subconscious encryption credential (error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            Marshal.FreeCoTaskMem(blobPointer);
            Marshal.FreeCoTaskMem(userPointer);
            Marshal.FreeCoTaskMem(targetPointer);
        }
    }

    private static string DecodeCredentialBlob(byte[] blob)
    {
        var unicode = Encoding.Unicode.GetString(blob).TrimEnd('\0');
        if (unicode.Length == 44)
        {
            return unicode;
        }

        var utf8 = Encoding.UTF8.GetString(blob).TrimEnd('\0');
        if (utf8.Length == 44)
        {
            return utf8;
        }

        throw new ModelConfigurationStoreException("The Subconscious encryption credential is not a valid Fernet key.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPointer);
}
