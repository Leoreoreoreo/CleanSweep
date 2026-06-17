using CleanSweep.Core.Cleaning;
using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;
using CleanSweep.Core.Services;

namespace CleanSweep.Core.Tests;

public sealed class CleanupModuleTests
{
    private const long MB = 1024L * 1024;

    private static ScanContext Ctx(IPlatformPaths paths) => new()
    {
        Paths = paths,
        Scanner = new FileSystemScanner(),
        Progress = null,
        Cancellation = CancellationToken.None
    };

    // ---- DirectoryCleanupModule (exercised via TempFilesModule) ----

    [Fact]
    public void DirectoryModule_lists_immediate_children_sized_and_sorted()
    {
        using var tree = new TempTree();
        var temp = tree.Dir("Temp");
        tree.Write("Temp/big.bin", 3000);
        tree.Write("Temp/small.bin", 1000);
        tree.Write("Temp/subcache/x.bin", 500);    // a directory child, sized recursively
        tree.Write("Temp/empty.bin", 0);           // zero bytes -> skipped
        tree.Dir("Temp/emptydir");                 // zero bytes -> skipped

        var paths = new FakePlatformPaths();
        paths.Temp.Add(temp);

        var result = new TempFilesModule().Scan(Ctx(paths));

        Assert.Equal(3, result.Count);
        // sorted by size descending
        Assert.Equal(new long[] { 3000, 1000, 500 }, result.Items.Select(i => i.SizeBytes).ToArray());
        Assert.Equal(4500, result.TotalBytes);
        // Temp files are a safe category -> pre-selected.
        Assert.All(result.Items, i => Assert.True(i.DefaultSelected));
        // the directory child is flagged as a directory
        Assert.True(result.Items.Single(i => i.SizeBytes == 500).IsDirectory);
    }

    [Fact]
    public void DirectoryModule_ignores_missing_target_dirs()
    {
        var paths = new FakePlatformPaths();
        paths.Temp.Add(Path.Combine(Path.GetTempPath(), "cleansweep-does-not-exist-" + Guid.NewGuid().ToString("N")));

        var result = new TempFilesModule().Scan(Ctx(paths));

        Assert.Equal(0, result.Count);
    }

    // ---- DevJunkModule ----

    [Fact]
    public void DevJunk_finds_build_dirs_sizes_them_and_does_not_descend()
    {
        using var tree = new TempTree();
        var devRoot = tree.Dir("projects");
        tree.Write("projects/app/node_modules/a.js", 5000);
        tree.Write("projects/app/node_modules/sub/node_modules/b.js", 1000); // nested -> must NOT be a separate item
        tree.Write("projects/app/obj/app.dll", 1500);
        tree.Write("projects/app/src/main.cs", 200);                          // not junk

        var paths = new FakePlatformPaths();
        paths.DevRoots.Add(devRoot);

        var result = new DevJunkModule().Scan(Ctx(paths));

        // Only the two top-level junk dirs, never the nested node_modules.
        Assert.Equal(2, result.Count);

        var nodeModules = result.Items.Single(i => i.DisplayName.StartsWith("node_modules"));
        Assert.Equal(6000, nodeModules.SizeBytes);   // 5000 + 1000 nested, counted as one
        Assert.True(nodeModules.IsDirectory);
        Assert.Contains("app", nodeModules.DisplayName); // "node_modules  -  in app"

        Assert.Contains(result.Items, i => i.DisplayName.StartsWith("obj") && i.SizeBytes == 1500);

        // Developer junk is risky -> never pre-selected.
        Assert.All(result.Items, i => Assert.False(i.DefaultSelected));
    }

    // ---- LargeFilesModule ----

    [Fact]
    public void LargeFiles_finds_only_files_at_or_above_threshold()
    {
        using var tree = new TempTree();
        var devRoot = tree.Dir("projects");
        tree.Write("projects/huge.iso", 101 * MB);     // >= 100 MB
        tree.Write("projects/normal.txt", 5000);       // small -> excluded
        tree.Write("projects/sub/big2.bin", 150 * MB); // >= 100 MB, nested

        var paths = new FakePlatformPaths { HomeDirectory = tree.Root };
        paths.DevRoots.Add(devRoot);

        var result = new LargeFilesModule().Scan(Ctx(paths));

        Assert.Equal(2, result.Count);
        Assert.All(result.Items, i =>
        {
            Assert.True(i.SizeBytes >= 100 * MB);
            Assert.False(i.IsDirectory);
            Assert.False(i.DefaultSelected);   // opt-in only
        });
        // sorted descending: 150 MB before 101 MB
        Assert.Equal(150 * MB, result.Items[0].SizeBytes);
        Assert.Equal(101 * MB, result.Items[1].SizeBytes);
    }
}
