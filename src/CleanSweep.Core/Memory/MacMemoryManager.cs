using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Memory;

/// <summary>
/// Reports memory via sysctl/vm_stat and frees inactive RAM by running `purge`.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacMemoryManager : IMemoryManager
{
    public MemoryStatus GetStatus()
    {
        long total = ReadSysctlLong("hw.memsize");
        long available = ReadAvailableFromVmStat();
        return new MemoryStatus { TotalBytes = total, AvailableBytes = available };
    }

    public MemoryFreeResult Free(IProgress<string>? progress)
    {
        var before = GetStatus();
        progress?.Report("Running purge to free inactive memory...");
        bool ok = RunPurge();
        var after = GetStatus();
        return new MemoryFreeResult
        {
            Before = before,
            After = after,
            Succeeded = ok,
            Message = ok
                ? "Ran purge to free inactive memory."
                : "purge was unavailable (it may require administrator rights)."
        };
    }

    private static long ReadSysctlLong(string key)
    {
        var output = Run("sysctl", $"-n {key}");
        return long.TryParse(output.Trim(), out var value) ? value : 0;
    }

    private static long ReadAvailableFromVmStat()
    {
        var output = Run("vm_stat", string.Empty);
        if (string.IsNullOrWhiteSpace(output)) return 0;

        long pageSize = 4096;
        var pageMatch = PageSizeRegex().Match(output);
        if (pageMatch.Success) long.TryParse(pageMatch.Groups[1].Value, out pageSize);

        long free = Pages(output, "Pages free");
        long inactive = Pages(output, "Pages inactive");
        long speculative = Pages(output, "Pages speculative");
        return (free + inactive + speculative) * pageSize;
    }

    private static long Pages(string vmStat, string label)
    {
        var match = Regex.Match(vmStat, Regex.Escape(label) + @":\s+(\d+)\.");
        return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private static bool RunPurge()
    {
        try
        {
            var psi = new ProcessStartInfo("purge") { UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return false;
            return p.WaitForExit(10000) && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string Run(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return string.Empty;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"page size of (\d+) bytes")]
    private static partial Regex PageSizeRegex();
}
