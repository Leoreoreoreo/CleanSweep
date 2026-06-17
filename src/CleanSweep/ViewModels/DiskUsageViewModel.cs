using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.Core;
using CleanSweep.Core.Platform;
using CleanSweep.Core.Services;
using CleanSweep.Services;

namespace CleanSweep.ViewModels;

public partial class DiskUsageViewModel : ViewModelBase
{
    // Largest folders are shown individually; the long tail is folded into one "Other" slice.
    private const int MaxSlices = 9;
    private static readonly string[] Palette =
    {
        "#3B82F6", "#06B6D4", "#8B5CF6", "#10B981", "#F59E0B",
        "#EC4899", "#14B8A6", "#6366F1", "#F472B6"
    };
    private static readonly SolidColorBrush OtherBrush = new(Color.Parse("#94A3B8"));

    private readonly IDialogService _dialogs;
    private readonly DiskUsageAnalyzer _analyzer = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<UsageSliceViewModel> Slices { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _rootPath = "";
    [ObservableProperty] private string _statusText = "Pick a folder and analyze it to see what is taking up space.";
    [ObservableProperty] private long _totalBytes;

    public string TotalText => ByteSize.Human(TotalBytes);
    public string RootName
    {
        get
        {
            if (string.IsNullOrEmpty(RootPath)) return "";
            var name = Path.GetFileName(RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? RootPath : name;
        }
    }

    public DiskUsageViewModel(IDialogService dialogs, IPlatformPaths paths)
    {
        _dialogs = dialogs;
        _rootPath = paths.HomeDirectory;
    }

    partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(TotalText));
    partial void OnRootPathChanged(string value) => OnPropertyChanged(nameof(RootName));

    [RelayCommand]
    private async Task Analyze() => await RunAsync(RootPath);

    [RelayCommand]
    private async Task ChooseFolder()
    {
        var picked = await _dialogs.PickFolderAsync("Choose a folder to analyze");
        if (string.IsNullOrWhiteSpace(picked)) return;
        RootPath = picked;
        await RunAsync(picked);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunAsync(string root)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(root)) return;
        IsBusy = true;
        HasResults = false;
        Slices.Clear();
        TotalBytes = 0;
        _cts = new CancellationTokenSource();
        try
        {
            StatusText = $"Measuring {RootName}...";
            var progress = new Progress<string>(s => StatusText = s);
            var entries = await _analyzer.AnalyzeAsync(root, progress, _cts.Token);
            Build(entries);
            HasResults = Slices.Count > 0;
            StatusText = HasResults
                ? $"{TotalText} across {Slices.Count} item(s) in {RootName}."
                : "Nothing measurable in this folder.";
        }
        catch (OperationCanceledException) { StatusText = "Analysis cancelled."; }
        catch (Exception ex) { StatusText = $"Analysis failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void Build(IReadOnlyList<UsageEntry> entries)
    {
        long total = entries.Sum(e => e.Bytes);
        TotalBytes = total;
        if (total <= 0) return;

        var top = entries.Take(MaxSlices).ToList();
        var tail = entries.Skip(MaxSlices).ToList();
        long tailBytes = tail.Sum(e => e.Bytes);

        double cursor = 0;
        for (int i = 0; i < top.Count; i++)
        {
            var e = top[i];
            double frac = (double)e.Bytes / total;
            var brush = new SolidColorBrush(Color.Parse(Palette[i % Palette.Length]));
            Slices.Add(new UsageSliceViewModel(e.Name, e.Path, e.Bytes, frac * 100, brush,
                DonutGeometry.Segment(cursor, cursor + frac), e.IsDirectory));
            cursor += frac;
        }

        if (tailBytes > 0)
        {
            double frac = (double)tailBytes / total;
            Slices.Add(new UsageSliceViewModel($"Other ({tail.Count} more)", "", tailBytes, frac * 100,
                OtherBrush, DonutGeometry.Segment(cursor, cursor + frac), isDirectory: false));
        }
    }
}

public partial class UsageSliceViewModel : ObservableObject
{
    public UsageSliceViewModel(string name, string path, long bytes, double percent,
                               IBrush brush, Geometry geometry, bool isDirectory)
    {
        Name = name;
        Path = path;
        Bytes = bytes;
        Percent = percent;
        Brush = brush;
        Geometry = geometry;
        IsDirectory = isDirectory;
    }

    public string Name { get; }
    public string Path { get; }
    public long Bytes { get; }
    public double Percent { get; }
    public IBrush Brush { get; }
    public Geometry Geometry { get; }
    public bool IsDirectory { get; }

    public string SizeText => ByteSize.Human(Bytes);
    public string PercentText => Percent >= 0.1 ? $"{Percent:0.#}%" : "<0.1%";
    public bool CanReveal => FileReveal.CanReveal(Path);

    [RelayCommand] private void Reveal() => FileReveal.Reveal(Path);
    [RelayCommand] private void Open() => FileReveal.Open(Path);
}

/// <summary>Builds the arc stroke for one donut segment (clockwise from 12 o'clock).</summary>
internal static class DonutGeometry
{
    private const double R = 84;       // ring radius
    private const double C = 100;      // centre of a 200x200 box
    private const double Gap = 0.004;  // blank fraction trimmed off each segment end

    public static Geometry Segment(double startFraction, double endFraction)
    {
        double a = startFraction, b = endFraction;
        if (b - a > 2 * Gap) { a += Gap; b -= Gap; }   // leave a hairline gap unless the slice is tiny
        b = Math.Min(b, a + 0.9999);                   // never close a full circle into an ambiguous arc

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(At(a), isFilled: false);
        ctx.ArcTo(At(b), new Size(R, R), 0, isLargeArc: (b - a) > 0.5, SweepDirection.Clockwise);
        return geometry;
    }

    private static Point At(double fraction)
    {
        double angle = fraction * 2 * Math.PI;
        return new Point(C + R * Math.Sin(angle), C - R * Math.Cos(angle));
    }
}
