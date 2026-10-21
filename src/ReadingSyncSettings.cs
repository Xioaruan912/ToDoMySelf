using System.Security.Cryptography;
using System.Text;

namespace PaperTodo;

public sealed class ReadingSyncSettings
{
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Folder { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    public bool AutoSync { get; set; }
    public bool Configured => !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Username) && EncryptedPassword.Length > 0;
}

public static class ReadingCredentials
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PaperTodo-Dev/Reading/WebDAV/v1");
    public static string Protect(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static string Unprotect(string encrypted)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
