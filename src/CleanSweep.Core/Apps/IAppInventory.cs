using CleanSweep.Core.Platform;

namespace CleanSweep.Core.Apps;

/// <summary>Lists installed applications and uninstalls them on request.</summary>
public interface IAppInventory
{
    /// <summary>False on platforms with no inventory implementation.</summary>
    bool IsSupported { get; }

    Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Uninstalls the app. Windows launches its uninstaller; macOS moves the
    /// bundle and its support files to the Trash. High-risk - confirm first.
    /// </summary>
    Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct);
}

public static class AppInventory
{
    public static IAppInventory Create(IPlatformPaths? paths = null)
    {
        if (OperatingSystem.IsWindows()) return new WindowsAppInventory();
        if (OperatingSystem.IsMacOS())   return new MacAppInventory(paths ?? PlatformPaths.Current);
        return new NullAppInventory();
    }
}

internal sealed class NullAppInventory : IAppInventory
{
    public bool IsSupported => false;
    public Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<InstalledApp>>(Array.Empty<InstalledApp>());
    public Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct)
        => Task.FromResult(new UninstallResult { Message = "App uninstall is not supported on this OS." });
}
