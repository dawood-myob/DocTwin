using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocTwin.Confluence;

namespace DocTwin.Security;

public sealed class StoredConnection
{
    public string Site { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool ScopedToken { get; set; }
    public string CloudId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public interface ICredentialStore
{
    StoredConnection? Read();
    void Save(ConnectionProfile profile, string token);
    void Remove();
}

public sealed class WindowsCredentialStore : ICredentialStore
{
    private readonly string _target;
    private const int NotFound = 1168;

    public WindowsCredentialStore() : this("DocTwin/Confluence/Primary") { }

    internal WindowsCredentialStore(string target) => _target = target;

    public StoredConnection? Read()
    {
        if (!CredRead(_target, 1, 0, out var pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NotFound)
                return null;
            throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize > 2560)
                throw new JsonException("Stored connection exceeds the supported size.");
            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return JsonSerializer.Deserialize<StoredConnection>(Encoding.Unicode.GetString(bytes))
                    ?? throw new JsonException("Stored connection is empty.");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CredFree(pointer); }
    }

    public void Save(ConnectionProfile profile, string token)
    {
        var stored = new StoredConnection
        {
            Site = profile.Site.AbsoluteUri, Email = profile.Email, ScopedToken = profile.ScopedToken,
            CloudId = profile.CloudId, Token = token
        };
        var bytes = Encoding.Unicode.GetBytes(JsonSerializer.Serialize(stored));
        try
        {
            if (bytes.Length > 2560)
                throw new ArgumentException("This token and profile exceed Windows Credential Manager's 2560-byte limit. Use the connection for this session without saving it.");
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new Credential
                {
                    Type = 1, TargetName = _target, UserName = profile.Email, Persist = 2,
                    CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob
                };
                if (!CredWrite(ref credential, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
                Marshal.FreeHGlobal(blob);
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Remove()
    {
        if (!CredDelete(_target, 1, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != NotFound)
                throw new Win32Exception(error);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
