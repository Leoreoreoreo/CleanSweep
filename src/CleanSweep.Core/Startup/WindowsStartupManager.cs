using System.Diagnostics;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CleanSweep.Core.Startup;

/// <summary>
/// Windows startup sources: the HKCU/HKLM Run keys (toggled via the
/// StartupApproved state Task Manager uses), the user/common Startup folders
/// (toggled by renaming, never deleting), and Task Scheduler logon tasks.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupManager : IStartupManager
{
    private const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string DisabledSuffix = ".disabled";

    public bool IsSupported => true;

    public Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken ct)
        => Task.Run<IReadOnlyList<StartupItem>>(() =>
        {
            var items = new List<StartupItem>();
            try { ReadRun(RegistryHive.CurrentUser, StartupScope.CurrentUser, items); } catch { }
            try { ReadRun(RegistryHive.LocalMachine, StartupScope.AllUsers, items); } catch { }
            try { ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupScope.CurrentUser, items); } catch { }
            try { ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupScope.AllUsers, items); } catch { }
            try { ReadLogonTasks(items, ct); } catch { }
            return items.OrderByDescending(i => i.IsEnabled).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    public Task<StartupActionResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct)
        => Task.Run(() => item.Kind switch
        {
            StartupKind.RegistryRun   => SetRunEnabled(item, enabled),
            StartupKind.StartupFolder => SetFolderEnabled(item, enabled),
            StartupKind.ScheduledTask => SetTaskEnabled(item, enabled),
            _ => Fail("This item cannot be toggled.")
        }, ct);

    // ---- Registry Run keys ----

    private static void ReadRun(RegistryHive hive, StartupScope scope, List<StartupItem> into)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var run = baseKey.OpenSubKey(RunPath);
        if (run is null) return;
        using var approved = baseKey.OpenSubKey(ApprovedRunPath);

        foreach (var name in run.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            into.Add(new StartupItem
            {
                Name = name,
                Command = run.GetValue(name) as string,
                IsEnabled = IsApprovedEnabled(approved, name),
                Kind = StartupKind.RegistryRun,
                Scope = scope,
                Location = $"{HiveTag(hive)}\\{RunPath}"
            });
        }
    }

    private static bool IsApprovedEnabled(RegistryKey? approved, string name)
    {
        // The 12-byte StartupApproved blob marks disabled with an odd low bit.
        if (approved?.GetValue(name) is byte[] b && b.Length > 0) return (b[0] & 1) == 0;
        return true; // no record -> enabled
    }

    private static StartupActionResult SetRunEnabled(StartupItem item, bool enabled)
    {
        var hive = item.Scope == StartupScope.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var approved = baseKey.CreateSubKey(ApprovedRunPath, writable: true);
            if (approved is null) return Fail("Could not open the startup-approval key.");

            byte[] blob = enabled
                ? new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                : new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            approved.SetValue(item.Name, blob, RegistryValueKind.Binary);
            return Ok(enabled ? $"Enabled “{item.Name}”." : $"Disabled “{item.Name}”.");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail("Administrator rights are required to change a machine-wide startup item.");
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    // ---- Startup folders (disable = rename, reversible) ----

    private static void ReadFolder(string folder, StartupScope scope, List<StartupItem> into)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

            bool disabled = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            var display = disabled ? fileName[..^DisabledSuffix.Length] : fileName;
            into.Add(new StartupItem
            {
                Name = Path.GetFileNameWithoutExtension(display),
                Command = file,
                IsEnabled = !disabled,
                Kind = StartupKind.StartupFolder,
                Scope = scope,
                Location = file
            });
        }
    }

    private static StartupActionResult SetFolderEnabled(StartupItem item, bool enabled)
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

    // ---- Task Scheduler logon tasks (locale-independent via XML) ----

    private static void ReadLogonTasks(List<StartupItem> into, CancellationToken ct)
    {
        var csv = Run("schtasks", out _, "/query", "/fo", "csv", "/nh");
        if (string.IsNullOrWhiteSpace(csv)) return;

        foreach (var line in csv.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            var name = FirstCsvField(line);
            if (string.IsNullOrEmpty(name) || name.Equals("TaskName", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue; // OS tasks

            var xml = Run("schtasks", out _, "/query", "/tn", name, "/xml");
            if (string.IsNullOrWhiteSpace(xml)) continue;

            var (isLogon, enabled) = InspectTaskXml(xml);
            if (!isLogon) continue;

            into.Add(new StartupItem
            {
                Name = name.TrimStart('\\'),
                Command = null,
                IsEnabled = enabled,
                Kind = StartupKind.ScheduledTask,
                Scope = StartupScope.AllUsers,
                Location = name
            });
        }
    }

    private static (bool isLogon, bool enabled) InspectTaskXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            bool isLogon = doc.Descendants().Any(e => e.Name.LocalName == "LogonTrigger");
            var enabledEl = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Enabled" && e.Parent?.Name.LocalName == "Settings");
            bool enabled = enabledEl is null
                || !string.Equals(enabledEl.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            return (isLogon, enabled);
        }
        catch { return (false, true); }
    }

    private static StartupActionResult SetTaskEnabled(StartupItem item, bool enabled)
    {
        Run("schtasks", out int exit, "/change", "/tn", item.Location, enabled ? "/enable" : "/disable");
        return exit == 0
            ? Ok(enabled ? $"Enabled task “{item.Name}”." : $"Disabled task “{item.Name}”.")
            : Fail($"schtasks could not change “{item.Name}” (it may require admin rights).");
    }

    // ---- helpers ----

    private static string FirstCsvField(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return string.Empty;
        if (line[0] == '"')
        {
            int end = line.IndexOf('"', 1);
            return end < 0 ? string.Empty : line[1..end];
        }
        int comma = line.IndexOf(',');
        return comma < 0 ? line : line[..comma];
    }

    private static string Run(string file, out int exitCode, params string[] args)
    {
        exitCode = -1;
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return string.Empty;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            exitCode = p.ExitCode;
            return output;
        }
        catch { return string.Empty; }
    }

    private static string HiveTag(RegistryHive hive) => hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
    private static StartupActionResult Ok(string message) => new() { Succeeded = true, Message = message };
    private static StartupActionResult Fail(string message) => new() { Succeeded = false, Message = message };
}
