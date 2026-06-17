namespace CleanSweep.Core.Startup;

public enum StartupScope { CurrentUser, AllUsers }

public enum StartupKind { RegistryRun, StartupFolder, ScheduledTask, LaunchAgent, LoginItem }

/// <summary>A program configured to run at login/startup.</summary>
public sealed class StartupItem
{
    public required string Name { get; init; }
    public string? Command { get; init; }
    public bool IsEnabled { get; init; }
    public StartupKind Kind { get; init; }
    public StartupScope Scope { get; init; }

    /// <summary>Opaque locator the owning manager uses to toggle this item
    /// (a registry path, a file path, or a task name).</summary>
    public required string Location { get; init; }

    public string KindLabel => Kind switch
    {
        StartupKind.RegistryRun   => "Registry (Run)",
        StartupKind.StartupFolder => "Startup folder",
        StartupKind.ScheduledTask => "Scheduled task",
        StartupKind.LaunchAgent   => "Launch agent",
        StartupKind.LoginItem     => "Login item",
        _ => Kind.ToString()
    };

    public string ScopeLabel => Scope == StartupScope.AllUsers ? "All users" : "This user";
}

public sealed class StartupActionResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
}
