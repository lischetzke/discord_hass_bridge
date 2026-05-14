using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DiscordHass.App;

internal static class AutostartManager
{
    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AppConstants.AutostartRegistryKey, writable: false);
        if (key is null) return false;
        return key.GetValue(AppConstants.AutostartValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AppConstants.AutostartRegistryKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(AppConstants.AutostartRegistryKey);
        if (key is null)
        {
            throw new InvalidOperationException("Unable to open HKCU Run registry key");
        }

        if (enabled)
        {
            string? exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath))
            {
                throw new InvalidOperationException("Cannot determine current executable path for autostart");
            }
            key.SetValue(AppConstants.AutostartValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppConstants.AutostartValueName, throwOnMissingValue: false);
        }
    }

    private static string? GetExecutablePath()
    {
        // Process.MainModule.FileName gives the actual .exe (works for single-file publish too).
        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
