namespace CleanSweep.Core.Services;

/// <summary>
/// Splits a Windows command line (e.g. a registry UninstallString) into its
/// executable and argument parts, honouring a leading quoted path.
/// </summary>
public static class CommandLine
{
    public static (string Executable, string Arguments) Split(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return (string.Empty, string.Empty);
        commandLine = commandLine.Trim();

        if (commandLine[0] == '"')
        {
            int end = commandLine.IndexOf('"', 1);
            if (end < 0) return (commandLine.Trim('"'), string.Empty);
            return (commandLine[1..end], commandLine[(end + 1)..].Trim());
        }

        int space = commandLine.IndexOf(' ');
        return space < 0
            ? (commandLine, string.Empty)
            : (commandLine[..space], commandLine[(space + 1)..].Trim());
    }
}
