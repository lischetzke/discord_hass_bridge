using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordHass.App;

namespace DiscordHass.Config;

internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public ConfigStore() : this(AppPaths.ConfigFile)
    {
    }

    public ConfigStore(string path)
    {
        _path = path;
    }

    public AppConfig Load()
    {
        if (!File.Exists(_path))
        {
            return new AppConfig();
        }

        try
        {
            string json = File.ReadAllText(_path);
            AppConfig? cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            return cfg ?? new AppConfig();
        }
        catch (Exception)
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        AppPaths.EnsureAppDataDirExists();
        string json = JsonSerializer.Serialize(config, JsonOptions);
        string tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_path))
        {
            File.Replace(tempPath, _path, null);
        }
        else
        {
            File.Move(tempPath, _path);
        }
    }
}
