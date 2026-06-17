using CleanSweep.Core.Models;
using CleanSweep.Core.Services;

namespace CleanSweep.Core.Tests;

public sealed class SafeDeleterTests
{
    private static SafeDeleter DeleterProtecting(params string[] protectedPaths)
    {
        var paths = new FakePlatformPaths();
        paths.Protected.AddRange(protectedPaths);
        return new SafeDeleter(paths);
    }

    [Fact]
    public void Protected_directory_itself_is_protected()
    {
        using var tree = new TempTree();
        var home = tree.Dir("home");
        var deleter = DeleterProtecting(home);

        Assert.True(deleter.IsProtected(home));
    }

    [Fact]
    public void Ancestor_of_a_protected_directory_is_protected()
    {
        using var tree = new TempTree();
        var home = tree.Dir("home", "user");          // protect …/home/user
        var deleter = DeleterProtecting(home);

        // Deleting the parent would take the protected dir with it.
        Assert.True(deleter.IsProtected(tree.Root));
        Assert.True(deleter.IsProtected(Path.Combine(tree.Root, "home")));
    }

    [Fact]
    public void Drive_root_is_protected()
    {
        using var tree = new TempTree();
        var deleter = DeleterProtecting(tree.Dir("home"));

        var driveRoot = Path.GetPathRoot(tree.Root)!;  // "C:\" on Windows, "/" on Unix
        Assert.True(deleter.IsProtected(driveRoot));
    }

    [Fact]
    public void Empty_or_blank_path_is_protected()
    {
        var deleter = DeleterProtecting();
        Assert.True(deleter.IsProtected(""));
        Assert.True(deleter.IsProtected("   "));
    }

    [Fact]
    public void Legitimate_temp_or_cache_child_is_not_protected()
    {
        using var tree = new TempTree();
        // The temp dir is NOT a protected location; its children are fair game.
        var tempDir = tree.Dir("AppData", "Local", "Temp");
        var child = tree.Dir("AppData", "Local", "Temp", "blob123");
        var deleter = DeleterProtecting(tree.Dir("home"));

        Assert.False(deleter.IsProtected(child));
        Assert.False(deleter.IsProtected(Path.Combine(tempDir, "session.tmp")));
    }

    [Fact]
    public void Delete_removes_a_file_and_counts_freed_bytes()
    {
        using var tree = new TempTree();
        var file = tree.Write("Temp/junk.bin", 2048);
        var deleter = DeleterProtecting(tree.Dir("home"));

        var item = new CleanItem
        {
            Path = file, DisplayName = "junk.bin", SizeBytes = 2048,
            Category = CleanCategory.TempFiles, IsDirectory = false
        };

        var outcome = deleter.Delete(new[] { item }, null, CancellationToken.None);

        Assert.False(File.Exists(file));
        Assert.Equal(1, outcome.DeletedCount);
        Assert.Equal(2048, outcome.FreedBytes);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public void Delete_removes_a_directory_recursively()
    {
        using var tree = new TempTree();
        var dir = tree.Dir("Temp", "cachedir");
        tree.Write("Temp/cachedir/a.bin", 100);
        tree.Write("Temp/cachedir/nested/b.bin", 200);
        var deleter = DeleterProtecting(tree.Dir("home"));

        var item = new CleanItem
        {
            Path = dir, DisplayName = "cachedir", SizeBytes = 300,
            Category = CleanCategory.AppCache, IsDirectory = true
        };

        var outcome = deleter.Delete(new[] { item }, null, CancellationToken.None);

        Assert.False(Directory.Exists(dir));
        Assert.Equal(1, outcome.DeletedCount);
    }

    [Fact]
    public void Delete_refuses_protected_items_and_leaves_them_intact()
    {
        using var tree = new TempTree();
        var home = tree.Dir("home");
        tree.Write("home/important.doc", 4096);
        var deleter = DeleterProtecting(home);

        var item = new CleanItem
        {
            Path = home, DisplayName = "home", SizeBytes = 4096,
            Category = CleanCategory.LargeFiles, IsDirectory = true
        };

        var outcome = deleter.Delete(new[] { item }, null, CancellationToken.None);

        Assert.True(Directory.Exists(home));        // untouched
        Assert.Equal(0, outcome.DeletedCount);
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Single(outcome.Errors);
    }
}
