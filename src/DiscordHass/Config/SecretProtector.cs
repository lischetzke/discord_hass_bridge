using System;
using System.Security.Cryptography;
using System.Text;

namespace DiscordHass.Config;

internal static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DiscordHass:v1");

    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
        {
            return null;
        }

        try
        {
            byte[] encrypted = Convert.FromBase64String(protectedBase64);
            byte[] bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
