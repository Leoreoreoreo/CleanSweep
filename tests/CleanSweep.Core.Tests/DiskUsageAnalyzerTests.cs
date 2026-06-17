using CleanSweep.Core.Services;

namespace CleanSweep.Core.Tests;

public sealed class DiskUsageAnalyzerTests
{
    [Fact]
    public async Task Measures_subfolders_and_loose_files_largest_first()
    {
        using var tree = new TempTree();
        tree.Write("big/a.bin", 3000);
        tree.Write("big/b.bin", 2000);   // big = 5000 bytes
        tree.Write("small/c.bin", 1000); // small = 1000 bytes
        tree.Write("loose.bin", 1500);   // a file directly under the root

        var entries = await new DiskUsageAnalyzer().AnalyzeAsync(tree.Root, null, CancellationToken.None);

        Assert.Equal(3, entries.Count);
        Assert.Equal("big", entries[0].Name); // sorted by size, descending
        Assert.Equal(5000, entries[0].Bytes);
        Assert.True(entries[0].IsDirectory);

        // Loose files are folded into a single non-directory entry.
        var loose = Assert.Single(entries, e => !e.IsDirectory);
        Assert.Equal(1500, loose.Bytes);
    }

    [Fact]
    public async Task A_missing_folder_yields_no_entries()
    {
        var ghost = Path.Combine(Path.GetTempPath(), "cleansweep-absent-" + Guid.NewGuid().ToString("N"));
        Assert.Empty(await new DiskUsageAnalyzer().AnalyzeAsync(ghost, null, CancellationToken.None));
    }
}
