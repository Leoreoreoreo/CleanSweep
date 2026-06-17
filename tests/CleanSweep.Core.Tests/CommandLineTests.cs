using CleanSweep.Core.Services;

namespace CleanSweep.Core.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Splits_quoted_executable_from_arguments()
    {
        var (exe, args) = CommandLine.Split("\"C:\\Program Files\\App\\uninstall.exe\" /S /quiet");
        Assert.Equal("C:\\Program Files\\App\\uninstall.exe", exe);
        Assert.Equal("/S /quiet", args);
    }

    [Fact]
    public void Splits_unquoted_executable()
    {
        var (exe, args) = CommandLine.Split("MsiExec.exe /X{0000-1111}");
        Assert.Equal("MsiExec.exe", exe);
        Assert.Equal("/X{0000-1111}", args);
    }

    [Fact]
    public void Executable_with_no_arguments()
    {
        var (exe, args) = CommandLine.Split("C:\\App\\uninstall.exe");
        Assert.Equal("C:\\App\\uninstall.exe", exe);
        Assert.Equal("", args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_empty_parts(string input)
    {
        var (exe, args) = CommandLine.Split(input);
        Assert.Equal("", exe);
        Assert.Equal("", args);
    }
}
