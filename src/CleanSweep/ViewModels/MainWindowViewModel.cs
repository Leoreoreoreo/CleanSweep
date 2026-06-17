using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.AI;
using CleanSweep.Core;
using CleanSweep.Core.AI;
using CleanSweep.Core.Apps;
using CleanSweep.Core.Cleaning;
using CleanSweep.Core.Memory;
using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;
using CleanSweep.Core.Services;
using CleanSweep.Core.Startup;
using CleanSweep.Services;

namespace CleanSweep.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ScanEngine _engine = new();
    private readonly IMemoryManager _memory = MemoryManagerFactory.Current;
    private readonly AiItemExplainer _explainer = ItemExplainerFactory.Create();
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _autoScanTimer;
    private CancellationTokenSource? _cts;

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    /// <summary>Phase 3 capabilities, each its own section.</summary>
    public DuplicatesViewModel Duplicates { get; }
    public DiskUsageViewModel DiskUsage { get; }
    public AppsViewModel Apps { get; }
    public StartupViewModel Startup { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _statusText = "Ready - click Smart Scan to find reclaimable space.";
    [ObservableProperty] private long _totalReclaimable;
    [ObservableProperty] private long _selectedBytes;

    [ObservableProperty] private double _memoryUsedPercent;
    [ObservableProperty] private string _memoryText = "";
    [ObservableProperty] private string _memoryDetail = "";

    // ---- Sidebar navigation ----
    // 0 Smart Scan · 1 Large Files · 2 Disk Usage · 3 Duplicates · 4 Apps · 5 Startup · 6 Memory · 7 Settings
    [ObservableProperty] private int _selectedSectionIndex;

    public bool IsSmartScanSection => SelectedSectionIndex == 0;
    public bool IsLargeFilesSection => SelectedSectionIndex == 1;
    public bool IsDiskUsageSection => SelectedSectionIndex == 2;
    public bool IsDuplicatesSection => SelectedSectionIndex == 3;
    public bool IsAppsSection => SelectedSectionIndex == 4;
    public bool IsStartupSection => SelectedSectionIndex == 5;
    public bool IsMemorySection => SelectedSectionIndex == 6;
    public bool IsSettingsSection => SelectedSectionIndex == 7;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSmartScanSection));
        OnPropertyChanged(nameof(IsLargeFilesSection));
        OnPropertyChanged(nameof(IsDiskUsageSection));
        OnPropertyChanged(nameof(IsDuplicatesSection));
        OnPropertyChanged(nameof(IsAppsSection));
        OnPropertyChanged(nameof(IsStartupSection));
        OnPropertyChanged(nameof(IsMemorySection));
        OnPropertyChanged(nameof(IsSettingsSection));
    }

    /// <summary>The Large Files category from the last scan, for its dedicated section.</summary>
    public CategoryViewModel? LargeFilesCategory =>
        Categories.FirstOrDefault(c => c.Category == CleanCategory.LargeFiles);
    public bool HasLargeFiles => LargeFilesCategory is not null;

    public string TotalReclaimableText => ByteSize.Human(TotalReclaimable);
    public string SelectedText => ByteSize.Human(SelectedBytes);
    /// <summary>Selected / total, for the reclaimable ring gauge.</summary>
    public double SelectedFraction => TotalReclaimable > 0 ? (double)SelectedBytes / TotalReclaimable : 0;

    public MainWindowViewModel() : this(new DialogService()) { }

    public MainWindowViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Settings = new SettingsViewModel(_explainer, new SettingsStore(), dialogs);
        Duplicates = new DuplicatesViewModel(_engine, PlatformPaths.Current, dialogs, () => Settings.ExcludedPaths);
        DiskUsage = new DiskUsageViewModel(dialogs, PlatformPaths.Current);
        Apps = new AppsViewModel(AppInventory.Create(), dialogs);
        Startup = new StartupViewModel(StartupManager.Create(), dialogs);

        _autoScanTimer = new DispatcherTimer();
        _autoScanTimer.Tick += OnAutoScanTick;
        Settings.AutoScanChanged += (_, _) => ConfigureAutoScan();
        ConfigureAutoScan();

        RefreshMemory();
    }

    private void ConfigureAutoScan()
    {
        _autoScanTimer.Stop();
        if (Settings.AutoScanInterval is { } interval)
        {
            _autoScanTimer.Interval = interval;
            _autoScanTimer.Start();
        }
    }

    private void OnAutoScanTick(object? sender, EventArgs e)
    {
        if (!IsBusy) _ = RunScanAsync();
    }

    partial void OnTotalReclaimableChanged(long value)
    {
        OnPropertyChanged(nameof(TotalReclaimableText));
        OnPropertyChanged(nameof(SelectedFraction));
    }
    partial void OnSelectedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectedFraction));
    }

    [RelayCommand]
    private async Task Scan() => await RunScanAsync();

    private async Task RunScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        HasResults = false;
        Categories.Clear();
        TotalReclaimable = 0;
        SelectedBytes = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s => StatusText = s);

        try
        {
            StatusText = "Scanning...";
            var onCategory = new Progress<CategoryResult>(AddCategory);
            await _engine.ScanAsync(progress, onCategory, Settings.ExcludedPaths, _cts.Token);
            UpdateTotals();
            HasResults = Categories.Count > 0;
            OnPropertyChanged(nameof(LargeFilesCategory));
            OnPropertyChanged(nameof(HasLargeFiles));
            StatusText = HasResults
                ? $"Found {ByteSize.Human(TotalReclaimable)} of reclaimable space across {Categories.Count} categories."
                : "All clean - nothing to reclaim.";
        }
        catch (OperationCanceledException) { StatusText = "Scan cancelled."; }
        catch (Exception ex) { StatusText = $"Scan failed: {ex.Message}"; }
        finally { IsBusy = false; RefreshMemory(); }
    }

    // Called as each category finishes scanning, so results fill in live.
    private void AddCategory(CategoryResult result)
    {
        var category = new CategoryViewModel(result, _explainer);
        category.PropertyChanged += OnCategoryChanged;
        Categories.Add(category);
        UpdateTotals();
        HasResults = true;
        OnPropertyChanged(nameof(LargeFilesCategory));
        OnPropertyChanged(nameof(HasLargeFiles));
        StatusText = $"Scanning... {ByteSize.Human(TotalReclaimable)} found so far";
    }

    [RelayCommand]
    private async Task Clean()
    {
        if (IsBusy || !HasResults) return;
        var selected = Categories.SelectMany(c => c.SelectedModels).ToList();
        if (selected.Count == 0) { StatusText = "Select something to clean first."; return; }

        long bytes = selected.Sum(i => i.SizeBytes);
        bool confirmed = await _dialogs.ConfirmAsync(
            "Clean now?",
            $"Permanently remove {selected.Count} item(s), freeing about {ByteSize.Human(bytes)}? Protected paths are always skipped.",
            "Clean", destructive: true);
        if (!confirmed) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        DeleteOutcome? outcome = null;
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            outcome = await _engine.CleanAsync(selected, progress, _cts.Token);
        }
        catch (Exception ex) { StatusText = $"Clean failed: {ex.Message}"; }
        finally { IsBusy = false; }

        if (outcome is not null)
        {
            var skipped = outcome.SkippedCount > 0 ? $"  ·  {outcome.SkippedCount} skipped" : "";
            StatusText = $"Cleaned {ByteSize.Human(outcome.FreedBytes)}  ·  removed {outcome.DeletedCount} item(s){skipped}.";
            await RunScanAsync();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void FreeMemory()
    {
        if (IsBusy) return;
        var progress = new Progress<string>(s => StatusText = s);
        var result = _memory.Free(progress);
        RefreshMemory();
        StatusText = result.Succeeded
            ? $"{result.Message}  Reclaimed ~{ByteSize.Human(result.FreedBytes)}."
            : result.Message;
    }

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategoryViewModel.SelectedBytes)) UpdateTotals();
    }

    private void UpdateTotals()
    {
        TotalReclaimable = Categories.Sum(c => c.TotalBytes);
        SelectedBytes = Categories.Sum(c => c.SelectedBytes);
    }

    private void RefreshMemory()
    {
        var status = _memory.GetStatus();
        MemoryUsedPercent = status.UsedPercent;
        MemoryText = status.TotalBytes > 0
            ? $"RAM  {ByteSize.Human(status.UsedBytes)} / {ByteSize.Human(status.TotalBytes)}  ({status.UsedPercent:0}%)"
            : "RAM usage unavailable";
        MemoryDetail = status.TotalBytes > 0 ? $"{ByteSize.Human(status.AvailableBytes)} available" : "";
    }
}
