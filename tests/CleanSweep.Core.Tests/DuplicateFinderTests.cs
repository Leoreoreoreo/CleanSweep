using CleanSweep.Core.Duplicates;

namespace CleanSweep.Core.Tests;

public sealed class DuplicateFinderTests
{
    // Tiny threshold so the synthesized files count as candidates.
    private static readonly DuplicateScanOptions Opt = new() { MinFileSizeBytes = 1, MaxDepth = 10 };

    private static void WriteText(TempTree tree, string rel, string content)
    {
        var path = Path.Combine(tree.Root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static async Task<IReadOnlyList<DuplicateGroup>> Find(TempTree tree, DuplicateScanOptions? opt = null)
        => await new DuplicateFinder().FindAsync(new[] { tree.Root }, opt ?? Opt, null, CancellationToken.None);

    [Fact]
    public async Task Groups_identical_files_and_ignores_unique_ones()
    {
        using var tree = new TempTree();
        WriteText(tree, "a/one.txt", "the same content here");
        WriteText(tree, "b/two.txt", "the same content here"); // identical to one.txt
        WriteText(tree, "c/three.txt", "the same content here"); // identical to one.txt
        WriteText(tree, "d/unique.txt", "totally different bytes");

        var groups = await Find(tree);

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Count);
        Assert.All(group.Files, f => Assert.Contains("content", File.ReadAllText(f.Path)));
        // keep one, remove two -> 2x file size reclaimable
        Assert.Equal(group.FileSizeBytes * 2, group.ReclaimableBytes);
    }

    [Fact]
    public async Task Same_size_different_content_is_not_a_duplicate()
    {
        using var tree = new TempTree();
        WriteText(tree, "a.bin", "AAAAAAAAAA"); // 10 bytes
        WriteText(tree, "b.bin", "BBBBBBBBBB"); // 10 bytes, same size, different content

        var groups = await Find(tree);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task Large_identical_files_are_grouped_via_partial_then_full_hash()
    {
        using var tree = new TempTree();
        // > 64 KB so the partial-hash then full-hash path is exercised.
        var content = new string('x', 200_000);
        WriteText(tree, "x/big1.dat", content);
        WriteText(tree, "y/big2.dat", content);
        WriteText(tree, "z/decoy.dat", content[..^1] + "Z"); // same length, differs only at the end

        var groups = await Find(tree);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count); // decoy excluded by the full hash
    }

    [Fact]
    public async Task Respects_minimum_size_and_skip_directories()
    {
        using var tree = new TempTree();
        WriteText(tree, "keep/a.txt", "duplicate body");
        WriteText(tree, "keep/b.txt", "duplicate body");
        WriteText(tree, "node_modules/x.txt", "ignored body"); // skipped dir
        WriteText(tree, "node_modules/y.txt", "ignored body");

        // Min size above our files -> nothing qualifies.
        Assert.Empty(await Find(tree, new DuplicateScanOptions { MinFileSizeBytes = 10_000 }));

        // Default skip list excludes node_modules; only keep/ dupes remain.
        var groups = await Find(tree, new DuplicateScanOptions { MinFileSizeBytes = 1 });
        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.DoesNotContain(group.Files, f => f.Path.Contains("node_modules"));
    }
}
