namespace CleanSweep.Core.Models;

/// <summary>A single reclaimable item (a file or a directory) found by a scan.</summary>
public sealed class CleanItem
{
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public long SizeBytes { get; init; }
    public CleanCategory Category { get; init; }
    public bool IsDirectory { get; init; }
    public DateTime? LastModified { get; init; }

    /// <summary>Whether this item is pre-selected for cleaning after a scan.</summary>
    public bool DefaultSelected { get; init; } = true;
}

/// <summary>All items found for one category in a scan.</summary>
public sealed class CategoryResult
{
    public required CleanCategory Category { get; init; }
    public List<CleanItem> Items { get; init; } = new();

    public long TotalBytes => Items.Sum(i => i.SizeBytes);
    public int Count => Items.Count;
}
