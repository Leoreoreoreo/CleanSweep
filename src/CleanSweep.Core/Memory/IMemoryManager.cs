using CleanSweep.Core.Models;

namespace CleanSweep.Core.Memory;

public interface IMemoryManager
{
    MemoryStatus GetStatus();
    MemoryFreeResult Free(IProgress<string>? progress);
}

public static class MemoryManagerFactory
{
    public static IMemoryManager Current { get; } = Create();

    private static IMemoryManager Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsMemoryManager();
        if (OperatingSystem.IsMacOS())   return new MacMemoryManager();
        return new NullMemoryManager();
    }
}

internal sealed class NullMemoryManager : IMemoryManager
{
    public MemoryStatus GetStatus() => new();
    public MemoryFreeResult Free(IProgress<string>? progress) => new()
    {
        Before = new MemoryStatus(),
        After = new MemoryStatus(),
        Succeeded = false,
        Message = "Memory management is not supported on this OS."
    };
}
