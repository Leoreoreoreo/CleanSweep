using CleanSweep.Core.Services;

namespace CleanSweep.Core.Tests;

public sealed class PathScopeTests
{
    private static string P(params string[] parts) => Path.Combine(parts);
    private static readonly string Base = Path.GetTempPath();

    [Fact]
    public void Path_under_an_excluded_root_is_matched()
    {
        var roots = PathScope.Normalize(new[] { P(Base, "excluded") });
        Assert.True(PathScope.IsUnderAny(P(Base, "excluded", "child", "f.txt"), roots));
    }

    [Fact]
    public void The_excluded_root_itself_is_matched()
    {
        var root = P(Base, "excluded");
        Assert.True(PathScope.IsUnderAny(root, PathScope.Normalize(new[] { root })));
    }

    [Fact]
    public void A_prefix_lookalike_is_not_a_child()
    {
        var roots = PathScope.Normalize(new[] { P(Base, "data") });
        Assert.False(PathScope.IsUnderAny(P(Base, "data-2", "f.txt"), roots));
        Assert.False(PathScope.IsUnderAny(P(Base, "other", "f.txt"), roots));
    }

    [Fact]
    public void Empty_root_set_matches_nothing()
        => Assert.False(PathScope.IsUnderAny(P(Base, "x"), PathScope.Normalize(Array.Empty<string>())));

    [Fact]
    public void Normalize_drops_blanks_and_duplicates()
    {
        var roots = PathScope.Normalize(new[] { "  ", "", P(Base, "a"), P(Base, "a") });
        Assert.Single(roots);
    }

    [Fact]
    public void Windows_matching_is_case_insensitive()
    {
        if (!OperatingSystem.IsWindows()) return; // POSIX paths are case-sensitive
        var roots = PathScope.Normalize(new[] { @"C:\Temp\Excluded" });
        Assert.True(PathScope.IsUnderAny(@"c:\temp\excluded\child", roots));
    }
}
