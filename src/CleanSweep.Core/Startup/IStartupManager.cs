using CleanSweep.Core.Platform;

namespace CleanSweep.Core.Startup;

/// <summary>
/// Lists login/startup items and toggles them. Toggling prefers a reversible
/// <em>disable</em> over deletion wherever the platform allows it.
/// </summary>
public interface IStartupManager
{
    bool IsSupported { get; }

    Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken ct);

    Task<StartupActionResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct);
}

public static class StartupManager
{
    public static IStartupManager Create(IPlatformPaths? paths = null)
    {
        if (OperatingSystem.IsWindows()) return new WindowsStartupManager();
        if (OperatingSystem.IsMacOS())   return new MacStartupManager(paths ?? PlatformPaths.Current);
        return new NullStartupManager();
    }
}

internal sealed class NullStartupManager : IStartupManager
{
    public bool IsSupported => false;
    public Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StartupItem>>(Array.Empty<StartupItem>());
    public Task<StartupActionResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct)
        => Task.FromResult(new StartupActionResult { Message = "Startup management is not supported on this OS." });
}
