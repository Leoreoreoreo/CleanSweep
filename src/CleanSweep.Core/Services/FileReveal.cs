using System.Diagnostics;

namespace CleanSweep.Core.Services;

/// <summary>Opens the OS file manager at an item, or opens the item itself. Best-effort, never throws.</summary>
public static class FileReveal
{
    /// <summary>True for paths we can actually show (not a sentinel like the Recycle Bin).</summary>
    public static bool CanReveal(string path)
        => !string.IsNullOrWhiteSpace(path) && !path.StartsWith("::");

    public static void Reveal(string path)
    {
        if (!CanReveal(path)) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Run("open", "-R", path);
            }
            else
            {
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;
                Run("xdg-open", dir);
            }
        }
        catch { /* best effort */ }
    }

    public static void Open(string path)
    {
        if (!CanReveal(path)) return;
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Run("open", path);
            else
                Run("xdg-open", path);
        }
        catch { /* best effort */ }
    }

    private static void Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
