using System;
using System.IO;
using System.Text.Json;

namespace CleanSweep.Services;

/// <summary>User settings persisted locally (e.g. the Anthropic API key + model override).</summary>
public sealed class AppSettings
{
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public string? Theme { get; set; }
}

/// <summary>Reads/writes <see cref="AppSettings"/> as JSON under the user's app-data folder.</summary>
public sealed class SettingsStore
{
    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanSweep");

    public string SettingsPath => Path.Combine(FolderPath, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { /* corrupt/unreadable — start fresh */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
