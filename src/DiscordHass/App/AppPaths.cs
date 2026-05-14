using System;
using System.IO;

namespace DiscordHass.App;

internal static class AppPaths
{
    public const string AppFolderName = "DiscordHass";

    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    public static string ConfigFile { get; } = Path.Combine(AppDataDir, "config.json");

    public static string LogFile { get; } = Path.Combine(AppDataDir, "discordhass.log");

    public static string UpdateStagingDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        "update");

    public static void EnsureAppDataDirExists()
    {
        Directory.CreateDirectory(AppDataDir);
    }
}
