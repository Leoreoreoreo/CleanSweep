using System.Diagnostics;
using System.Runtime.Versioning;
using System.Xml.Linq;
using CleanSweep.Core.Platform;

namespace CleanSweep.Core.Startup;

/// <summary>
/// macOS startup sources: per-user LaunchAgents (~/Library/LaunchAgents,
/// disabled by renaming so it's reversible) and System Events login items
/// (which can only be listed and removed).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacStartupManager : IStartupManager
{
    private const string DisabledSuffix = ".disabled";
    private readonly IPlatformPaths _paths;

    public MacStartupManager(IPlatformPaths paths) => _paths = paths;

    public bool IsSupported => true;

    private string LaunchAgentsDir => Path.Combine(_paths.HomeDirectory, "Library", "LaunchAgents");

    public Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken ct)
        => Task.Run<IReadOnlyList<StartupItem>>(() =>
        {
            var items = new List<StartupItem>();
            try { ReadLaunchAgents(items); } catch { }
            try { ReadLoginItems(items); } catch { }
            return items.OrderByDescending(i => i.IsEnabled).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    public Task<StartupActionResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct)
        => Task.Run(() => item.Kind switch
        {
            StartupKind.LaunchAgent => SetLaunchAgentEnabled(item, enabled),
            StartupKind.LoginItem   => SetLoginItemEnabled(item, enabled),
            _ => Fail("This item cannot be toggled.")
        }, ct);

    // ---- LaunchAgents (disable = rename, reversible) ----

    private void ReadLaunchAgents(List<StartupItem> into)
    {
        var dir = LaunchAgentsDir;
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var fileName = Path.GetFileName(file);
            bool disabled = fileName.EndsWith(".plist" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            bool isPlist = fileName.EndsWith(".plist", StringComparison.OrdinalIgnoreCase);
            if (!isPlist && !disabled) continue;

            var baseName = disabled ? fileName[..^DisabledSuffix.Length] : fileName;
            into.Add(new StartupItem
            {
                Name = ReadLabel(file) ?? Path.GetFileNameWithoutExtension(baseName),
                Command = file,
                IsEnabled = !disabled,
                Kind = StartupKind.LaunchAgent,
                Scope = StartupScope.CurrentUser,
                Location = file
            });
        }
    }

    private static string? ReadLabel(string plistFile)
    {
        try
        {
            var doc = XDocument.Parse(File.ReadAllText(plistFile)); // XML plists only; binary falls back to filename
            var keys = doc.Descendants().Where(e => e.Name.LocalName == "key").ToList();
            foreach (var key in keys)
                if (key.Value == "Label" && key.ElementsAfterSelf().FirstOrDefault() is { } v)
                    return v.Value;
        }
        catch { /* binary plist or unreadable — caller falls back to the filename */ }
        return null;
    }

    private StartupActionResult SetLaunchAgentEnabled(StartupItem item, bool enabled)
    {
        try
        {
            var path = item.Location;
            bool isDisabledFile = path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

            if (enabled && isDisabledFile)
                File.Move(path, path[..^DisabledSuffix.Length]);
            else if (!enabled && !isDisabledFile)
                File.Move(path, path + DisabledSuffix);
            else
                return Ok($"“{item.Name}” is already {(enabled ? "enabled" : "disabled")}.");

            return Ok(enabled ? $"Enabled “{item.Name}”." : $"Disabled “{item.Name}”.");
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    // ---- Login items (list + remove only) ----

    private static void ReadLoginItems(List<StartupItem> into)
    {
        var names = RunOsa("tell application \"System Events\" to get the name of every login item", out _);
        if (string.IsNullOrWhiteSpace(names)) return;

        foreach (var raw in names.Split(','))
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            into.Add(new StartupItem
            {
                Name = name,
                Command = null,
                IsEnabled = true,
                Kind = StartupKind.LoginItem,
                Scope = StartupScope.CurrentUser,
                Location = name
            });
        }
    }

    private static StartupActionResult SetLoginItemEnabled(StartupItem item, bool enabled)
    {
        if (enabled)
            return Fail("Login items can't be re-enabled automatically — re-add it from System Settings.");

        var escaped = item.Location.Replace("\"", "\\\"");
        RunOsa($"tell application \"System Events\" to delete login item \"{escaped}\"", out int exit);
        return exit == 0 ? Ok($"Removed login item “{item.Name}”.") : Fail($"Could not remove “{item.Name}”.");
    }

    // ---- helpers ----

    private static string RunOsa(string script, out int exitCode)
    {
        exitCode = -1;
        try
        {
            var psi = new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);
            using var p = Process.Start(psi);
            if (p is null) return string.Empty;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            exitCode = p.ExitCode;
            return output;
        }
        catch { return string.Empty; }
    }

    private static StartupActionResult Ok(string message) => new() { Succeeded = true, Message = message };
    private static StartupActionResult Fail(string message) => new() { Succeeded = false, Message = message };
}
