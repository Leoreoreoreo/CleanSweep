namespace CleanSweep.Core.Duplicates;

/// <summary>One copy of a file that has at least one identical twin elsewhere.</summary>
public sealed class DuplicateFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long SizeBytes { get; init; }
    public DateTime? LastModified { get; init; }
}

/// <summary>A set of byte-for-byte identical files (same size + same content hash).</summary>
public sealed class DuplicateGroup
{
    /// <summary>Content hash shared by every file in the group (hex).</summary>
    public required string ContentHash { get; init; }

    /// <summary>The size of each individual file in the group.</summary>
    public long FileSizeBytes { get; init; }

    public required IReadOnlyList<DuplicateFile> Files { get; init; }

    public int Count => Files.Count;

    /// <summary>Bytes reclaimable by keeping a single copy and removing the rest.</summary>
    public long ReclaimableBytes => FileSizeBytes * Math.Max(0, Count - 1);
}
