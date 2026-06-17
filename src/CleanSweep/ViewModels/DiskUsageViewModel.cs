using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
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
    private const int RevealFrames = 38; // ~0.6s of count-up + grow-in at 16 ms/frame
    private static readonly string[] Palette =
    {
        "#3B82F6", "#06B6D4", "#8B5CF6", "#10B981", "#F59E0B",
        "#EC4899", "#14B8A6", "#6366F1", "#F472B6"
    };
    private static readonly SolidColorBrush OtherBrush = new(Color.Parse("#94A3B8"));

    private readonly IDialogService _dialogs;
    private readonly DiskUsageAnalyzer _analyzer = new();
    private readonly DispatcherTimer _reveal = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private int _revealFrame;
    private CancellationTokenSource? _cts;

    public ObservableCollection<UsageSliceViewModel> Slices { get; } = new();
    public ObservableCollection<CrumbViewModel> Breadcrumbs { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _rootPath = "";
    [ObservableProperty] private string _statusText = "Pick a folder and analyze it to see what is taking up space.";
    [ObservableProperty] private long _totalBytes;

    // Reveal animation (driven by a timer so it replays on every analysis).
    [ObservableProperty] private double _displayTotalBytes;
    [ObservableProperty] private double _revealOpacity = 1;
    [ObservableProperty] private double _revealScale = 1;

    public string TotalText => ByteSize.Human((long)DisplayTotalBytes);
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
        _reveal.Tick += OnRevealTick;
    }

    partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(TotalText));
    partial void OnDisplayTotalBytesChanged(double value) => OnPropertyChanged(nameof(TotalText));
    partial void OnRootPathChanged(string value) => OnPropertyChanged(nameof(RootName));

    [RelayCommand]
    private async Task Analyze() => await RunAsync(RootPath);

    [RelayCommand]
    private async Task ChooseFolder()
    {
        var picked = await _dialogs.PickFolderAsync("Choose a folder to analyze");
        if (string.IsNullOrWhiteSpace(picked)) return;
        await RunAsync(picked);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Navigate to (and analyze) another folder - used by breadcrumbs and drill-down.</summary>
    private void NavigateTo(string path)
    {
        if (!IsBusy && !string.IsNullOrWhiteSpace(path)) _ = RunAsync(path);
    }

    private async Task RunAsync(string root)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(root)) return;
        RootPath = root;
        IsBusy = true;
        HasResults = false;
        _reveal.Stop();
        Slices.Clear();
        TotalBytes = 0;
        BuildBreadcrumbs(root);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            StatusText = $"Measuring {RootName}...";
            var progress = new Progress<string>(s => StatusText = s);
            var entries = await _analyzer.AnalyzeAsync(root, progress, _cts.Token);
            Build(entries);
            HasResults = Slices.Count > 0;
            StatusText = HasResults
                ? $"{ByteSize.Human(TotalBytes)} across {Slices.Count} item(s) in {RootName}. Click a folder to go deeper."
                : "Nothing measurable in this folder.";
            if (HasResults) StartReveal();
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
                DonutGeometry.Segment(cursor, cursor + frac), e.IsDirectory, NavigateTo));
            cursor += frac;
        }

        if (tailBytes > 0)
        {
            double frac = (double)tailBytes / total;
            Slices.Add(new UsageSliceViewModel($"Other ({tail.Count} more)", "", tailBytes, frac * 100,
                OtherBrush, DonutGeometry.Segment(cursor, cursor + frac), isDirectory: false, onDrill: null));
        }
    }

    private void BuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        var chain = new List<(string Name, string Full)>();
        try
        {
            for (var di = new DirectoryInfo(path); di is not null; di = di.Parent)
            {
                var name = string.IsNullOrEmpty(di.Name) ? di.FullName : di.Name; // a drive root shows e.g. "C:\"
                chain.Add((name, di.FullName));
            }
        }
        catch { /* unreadable path - leave the trail empty */ }

        chain.Reverse();
        for (int i = 0; i < chain.Count; i++)
            Breadcrumbs.Add(new CrumbViewModel(chain[i].Name, chain[i].Full, NavigateTo, isLast: i == chain.Count - 1));
    }

    private void StartReveal()
    {
        _revealFrame = 0;
        DisplayTotalBytes = 0;
        RevealOpacity = 0;
        RevealScale = 0.72;
        _reveal.Start();
    }

    private void OnRevealTick(object? sender, EventArgs e)
    {
        _revealFrame++;
        double t = Math.Min(1.0, (double)_revealFrame / RevealFrames);
        double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        DisplayTotalBytes = TotalBytes * eased;
        RevealOpacity = eased;
        RevealScale = 0.72 + 0.28 * eased;
        if (t >= 1.0)
        {
            _reveal.Stop();
            DisplayTotalBytes = TotalBytes;
            RevealOpacity = 1;
            RevealScale = 1;
        }
    }
}

public partial class UsageSliceViewModel : ObservableObject
{
    private readonly Action<string>? _onDrill;

    public UsageSliceViewModel(string name, string path, long bytes, double percent,
                               IBrush brush, Geometry geometry, bool isDirectory, Action<string>? onDrill)
    {
        Name = name;
        Path = path;
        Bytes = bytes;
        Percent = percent;
        Brush = brush;
        Geometry = geometry;
        IsDirectory = isDirectory;
        _onDrill = onDrill;
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
    public bool CanDrill => _onDrill is not null && IsDirectory && !string.IsNullOrEmpty(Path) && Directory.Exists(Path);

    [RelayCommand] private void Drill() { if (CanDrill) _onDrill!(Path); }
    [RelayCommand] private void Reveal() => FileReveal.Reveal(Path);
    [RelayCommand] private void Open() => FileReveal.Open(Path);
}

/// <summary>One clickable step in the Disk Usage path trail.</summary>
public partial class CrumbViewModel : ObservableObject
{
    private readonly Action<string> _go;

    public CrumbViewModel(string name, string path, Action<string> go, bool isLast)
    {
        Name = name;
        Path = path;
        IsLast = isLast;
        _go = go;
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsLast { get; }

    [RelayCommand] private void Go() => _go(Path);
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
