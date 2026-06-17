using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Memory;

/// <summary>
/// Reports physical memory via GlobalMemoryStatusEx and frees RAM by trimming
/// the working set of every accessible process (EmptyWorkingSet).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryManager : IMemoryManager
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public MemoryStatus GetStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return new MemoryStatus();
        return new MemoryStatus
        {
            TotalBytes = (long)m.ullTotalPhys,
            AvailableBytes = (long)m.ullAvailPhys
        };
    }

    public MemoryFreeResult Free(IProgress<string>? progress)
    {
        var before = GetStatus();
        int trimmed = 0;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (EmptyWorkingSet(proc.Handle)) trimmed++;
            }
            catch
            {
                // protected / exited process — skip
            }
            finally
            {
                proc.Dispose();
            }
        }

        progress?.Report($"Trimmed working sets of {trimmed} processes");
        var after = GetStatus();
        return new MemoryFreeResult
        {
            Before = before,
            After = after,
            Succeeded = true,
            Message = $"Trimmed working sets of {trimmed} processes."
        };
    }
}
