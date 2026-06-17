namespace CleanSweep.Core.Apps;

/// <summary>An installed application discovered by an <see cref="IAppInventory"/>.</summary>
public sealed class InstalledApp
{
    public required string Name { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }

    /// <summary>Best-effort on-disk size; 0 when unknown.</summary>
    public long SizeBytes { get; init; }

    public string? InstallLocation { get; init; }

    /// <summary>Windows: the uninstall command line. macOS: the .app bundle path.</summary>
    public required string UninstallTarget { get; init; }

    /// <summary>Where it came from — for display/grouping (e.g. "HKLM", "/Applications").</summary>
    public string Source { get; init; } = string.Empty;

    public string SizeText => SizeBytes > 0 ? ByteSize.Human(SizeBytes) : "—";
}

/// <summary>Outcome of an uninstall request.</summary>
public sealed class UninstallResult
{
    public bool Started { get; init; }
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public long FreedBytes { get; init; }
}
