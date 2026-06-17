namespace CleanSweep.Core.Models;

/// <summary>A snapshot of physical memory usage.</summary>
public sealed class MemoryStatus
{
    public long TotalBytes { get; init; }
    public long AvailableBytes { get; init; }

    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);
    public double UsedPercent => TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes * 100.0;
}

/// <summary>The outcome of a "free up memory" operation.</summary>
public sealed class MemoryFreeResult
{
    public required MemoryStatus Before { get; init; }
    public required MemoryStatus After { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool Succeeded { get; init; } = true;

    /// <summary>Bytes that became available (clamped at zero).</summary>
    public long FreedBytes => Math.Max(0, After.AvailableBytes - Before.AvailableBytes);
}
