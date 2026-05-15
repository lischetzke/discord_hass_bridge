using System;
using System.Collections.Generic;
using System.IO;

namespace DiscordHass.App;

/// <summary>
/// Pure (testable) parsing helpers for Windows' Capability Access Manager registry
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged\).
/// </summary>
///
/// Background: Windows tracks per-app camera usage in this registry hive. Each subkey
/// of NonPackaged is named after the executable path with backslashes replaced by '#'.
/// Each subkey carries two QWORD values, LastUsedTimeStart and LastUsedTimeStop, both
/// encoded as Windows FILETIME. While the app is actively using the camera,
/// LastUsedTimeStop is 0; once the app releases the camera, Windows writes the
/// release time there. This is the same signal that powers the Windows "camera in use"
/// tray indicator and the Settings → Privacy → Camera page.
///
/// References (verified May 2026):
///   https://svch0st.medium.com/can-you-track-processes-accessing-the-camera-and-microphone-7e6885b37072
///   https://davidarno.org/using-the-registry-to-monitor-webcam-and-microphone-use/
///   https://docs.velociraptor.app/exchange/artifacts/pages/windows.registry.capabilityaccessmanager/
internal readonly record struct CapabilityAccessEntry(
    string SubkeyName,
    long LastUsedTimeStart,
    long LastUsedTimeStop);

internal static class CapabilityAccessParser
{
    /// <summary>Discord's known exe basenames across Stable/PTB/Canary channels.</summary>
    public static readonly IReadOnlySet<string> DiscordExeBasenames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Discord.exe",
            "DiscordPTB.exe",
            "DiscordCanary.exe",
            "DiscordDevelopment.exe",
        };

    /// <summary>
    /// Returns the exe path (decoded) of the first matching app currently using the camera,
    /// or null if none of the target basenames are currently in use.
    /// </summary>
    public static string? FindInUseExe(
        IEnumerable<CapabilityAccessEntry> entries,
        IReadOnlySet<string> targetBasenames)
    {
        foreach (CapabilityAccessEntry e in entries)
        {
            if (!IsInUse(e)) continue;
            string exePath = DecodeKeyName(e.SubkeyName);
            string basename = Path.GetFileName(exePath);
            if (targetBasenames.Contains(basename))
            {
                return exePath;
            }
        }
        return null;
    }

    /// <summary>An entry is "in use" iff Start &gt; 0 and Stop == 0.</summary>
    public static bool IsInUse(CapabilityAccessEntry entry)
        => entry.LastUsedTimeStart > 0 && entry.LastUsedTimeStop == 0;

    /// <summary>Subkey-name → file path: replace '#' with '\'.</summary>
    public static string DecodeKeyName(string keyName)
        => keyName.Replace('#', '\\');

    /// <summary>File path → subkey-name: replace '\' with '#' (inverse of <see cref="DecodeKeyName"/>).</summary>
    public static string EncodeKeyName(string exePath)
        => exePath.Replace('\\', '#');

    /// <summary>
    /// Coerces a value read from the registry into a long. The CapabilityAccessManager
    /// values are normally REG_QWORD (returned as long), but some tools / atomic tests
    /// have been observed writing them as REG_BINARY 8-byte blobs — accept both.
    /// </summary>
    public static long AsFileTimeLong(object? value)
    {
        return value switch
        {
            long l => l,
            int i => i,
            uint u => u,
            byte[] bytes when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0),
            _ => 0,
        };
    }
}
