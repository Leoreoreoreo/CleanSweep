using System.Diagnostics;
using System.Runtime.Versioning;
using CleanSweep.Core.Platform;
using CleanSweep.Core.Services;

namespace CleanSweep.Core.Apps;

/// <summary>
/// Lists .app bundles under /Applications and ~/Applications. Uninstalling moves
/// the bundle plus its associated ~/Library support files to the Trash (so the
/// action is reversible), guarded by <see cref="SafeDeleter"/>.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacAppInventory : IAppInventory
{
    private readonly IPlatformPaths _paths;
    private readonly FileSystemScanner _scanner = new();

    public MacAppInventory(IPlatformPaths paths) => _paths = paths;

    public bool IsSupported => true;

    private IEnumerable<string> AppDirs() => new[]
    {
        "/Applications",
        Path.Combine(_paths.HomeDirectory, "Applications"),
    };

    public Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken ct)
        => Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var apps = new List<InstalledApp>();
            foreach (var dir in AppDirs())
            {
                if (!Directory.Exists(dir)) continue;
                List<string> bundles;
                try { bundles = Directory.EnumerateDirectories(dir, "*.app").ToList(); }
                catch { continue; }

                foreach (var bundle in bundles)
                {
                    ct.ThrowIfCancellationRequested();
                    apps.Add(ReadBundle(bundle, dir, ct));
                }
            }
            return apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    private InstalledApp ReadBundle(string bundle, string source, CancellationToken ct)
    {
        var infoBase = Path.Combine(bundle, "Contents", "Info"); // `defaults` wants the path sans .plist
        return new InstalledApp
        {
            Name = Path.GetFileNameWithoutExtension(bundle),
            Version = ReadDefault(infoBase, "CFBundleShortVersionString"),
            Publisher = ReadDefault(infoBase, "CFBundleIdentifier"), // bundle id is our best cheap identifier
            SizeBytes = _scanner.GetDirectorySize(bundle, ct),
            InstallLocation = bundle,
            UninstallTarget = bundle,
            Source = source
        };
    }

    public Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct)
        => Task.Run(() =>
        {
            var deleter = new SafeDeleter(_paths);
            var home = _paths.HomeDirectory;
            var id = app.Publisher; // the bundle identifier captured during listing

            var targets = new List<string> { app.UninstallTarget };
            if (!string.IsNullOrWhiteSpace(id))
            {
                targets.Add(Path.Combine(home, "Library", "Caches", id));
                targets.Add(Path.Combine(home, "Library", "Preferences", id + ".plist"));
                targets.Add(Path.Combine(home, "Library", "Containers", id));
                targets.Add(Path.Combine(home, "Library", "Application Support", id));
                targets.Add(Path.Combine(home, "Library", "Saved Application State", id + ".savedState"));
            }

            long freed = 0;
            int moved = 0;
            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                if (deleter.IsProtected(target)) continue;           // last-line safety
                bool isDir = Directory.Exists(target);
                if (!isDir && !File.Exists(target)) continue;

                long size = isDir ? _scanner.GetDirectorySize(target, ct) : _scanner.GetFileSize(target);
                if (MoveToTrash(target)) { freed += size; moved++; }
            }

            return new UninstallResult
            {
                Started = true,
                Succeeded = moved > 0,
                FreedBytes = freed,
                Message = moved > 0
                    ? $"Moved {app.Name} and {moved - 1} support item(s) to the Trash."
                    : $"Could not move {app.Name} to the Trash (it may require admin rights)."
            };
        }, ct);

    private bool MoveToTrash(string path)
    {
        try
        {
            var trash = Path.Combine(_paths.HomeDirectory, ".Trash");
            Directory.CreateDirectory(trash);

            var dest = Path.Combine(trash, Path.GetFileName(path));
            int n = 1;
            while (File.Exists(dest) || Directory.Exists(dest))
                dest = Path.Combine(trash, $"{Path.GetFileNameWithoutExtension(path)} {n++}{Path.GetExtension(path)}");

            Directory.Move(path, dest); // works for files and directories on the same volume
            return true;
        }
        catch { return false; }
    }

    private static string? ReadDefault(string plistBasePath, string key)
    {
        var value = Run("defaults", "read", plistBasePath, key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return string.Empty;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch { return string.Empty; }
    }
}
