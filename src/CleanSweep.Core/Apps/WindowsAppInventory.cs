using System.Diagnostics;
using System.Runtime.Versioning;
using CleanSweep.Core.Services;
using Microsoft.Win32;

namespace CleanSweep.Core.Apps;

/// <summary>
/// Enumerates installed apps from the Windows uninstall registry keys
/// (HKLM 64/32-bit and HKCU) and launches an app's own uninstaller on request.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppInventory : IAppInventory
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public bool IsSupported => true;

    public Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken ct)
        => Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var found = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            // Registry32 view transparently maps to WOW6432Node for 32-bit apps.
            Read(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", found, ct);
            Read(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM", found, ct);
            Read(RegistryHive.CurrentUser,  RegistryView.Registry64, "HKCU", found, ct);

            return found.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    private static void Read(RegistryHive hive, RegistryView view, string source,
                             Dictionary<string, InstalledApp> into, CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall is null) return;

            foreach (var subName in uninstall.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var key = uninstall.OpenSubKey(subName);
                    if (key is null) continue;
                    var app = Parse(key, source);
                    if (app is not null && !into.ContainsKey(app.Name)) into[app.Name] = app;
                }
                catch { /* unreadable entry — skip */ }
            }
        }
        catch { /* hive/key unavailable — skip */ }
    }

    private static InstalledApp? Parse(RegistryKey key, string source)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrEmpty(name)) return null;

        // Hide OS plumbing and Windows updates.
        if (key.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (key.GetValue("ParentKeyName") is string parent && !string.IsNullOrEmpty(parent)) return null;
        if (key.GetValue("ReleaseType") is "Security Update" or "Update" or "Hotfix") return null;

        var uninstall = (key.GetValue("QuietUninstallString") as string)
                     ?? (key.GetValue("UninstallString") as string);
        if (string.IsNullOrWhiteSpace(uninstall)) return null; // nothing actionable

        long size = key.GetValue("EstimatedSize") is int kb && kb > 0 ? (long)kb * 1024 : 0;

        return new InstalledApp
        {
            Name = name,
            Publisher = (key.GetValue("Publisher") as string)?.Trim(),
            Version = (key.GetValue("DisplayVersion") as string)?.Trim(),
            SizeBytes = size,
            InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim(),
            UninstallTarget = uninstall.Trim(),
            Source = source
        };
    }

    public Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct)
        => Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(app.UninstallTarget))
                return new UninstallResult { Message = $"No uninstall command for {app.Name}." };

            try
            {
                var (exe, args) = CommandLine.Split(app.UninstallTarget);
                if (string.IsNullOrEmpty(exe))
                    return new UninstallResult { Message = $"Could not parse the uninstall command for {app.Name}." };

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true // allow the installer's own UAC elevation prompt
                });

                return new UninstallResult
                {
                    Started = true,
                    Succeeded = true,
                    Message = $"Launched the uninstaller for {app.Name}. Follow its prompts to finish."
                };
            }
            catch (Exception ex)
            {
                return new UninstallResult { Message = $"Failed to launch uninstaller: {ex.Message}" };
            }
        }, ct);
}
