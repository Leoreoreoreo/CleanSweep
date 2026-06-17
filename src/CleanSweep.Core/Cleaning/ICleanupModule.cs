using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;
using CleanSweep.Core.Services;

namespace CleanSweep.Core.Cleaning;

/// <summary>Shared state handed to every module during a scan.</summary>
public sealed class ScanContext
{
    public required IPlatformPaths Paths { get; init; }
    public required FileSystemScanner Scanner { get; init; }
    public IProgress<string>? Progress { get; init; }
    public CancellationToken Cancellation { get; init; }
}

/// <summary>A unit of scanning logic for one <see cref="CleanCategory"/>.</summary>
public interface ICleanupModule
{
    string Name { get; }
    CleanCategory Category { get; }
    CategoryResult Scan(ScanContext ctx);
}

/// <summary>Magic paths that need special (non-filesystem) delete handling.</summary>
public static class Sentinels
{
    public const string RecycleBin = "::CleanSweep:RecycleBin::";
}
