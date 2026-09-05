using System.Security.Cryptography;
using System.Text;

namespace Bibliognost.Security;

public static class ProtectedSecretStore
{
    // Keep the original purpose for backwards compatibility with sessions saved by 0.2.
    private static readonly byte[] XmaEntropy = Encoding.UTF8.GetBytes("Bibliognost.XMA.Session.v1");
    private static readonly byte[] NexusEntropy = Encoding.UTF8.GetBytes("Bibliognost.Nexus.ApiKey.v1");

    public static string Protect(string plaintext) => Protect(plaintext, XmaEntropy);
    public static string? TryUnprotect(string? encrypted) => TryUnprotect(encrypted, XmaEntropy);
    public static string ProtectNexus(string plaintext) => Protect(plaintext, NexusEntropy);
    public static string? TryUnprotectNexus(string? encrypted) => TryUnprotect(encrypted, NexusEntropy);

    private static string Protect(string plaintext, byte[] entropy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        var bytes = Encoding.UTF8.GetBytes(plaintext.Trim());
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string? TryUnprotect(string? encrypted, byte[] entropy)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return null;
        byte[]? clear = null;
        try
        {
            clear = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
        finally
        {
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }
}
