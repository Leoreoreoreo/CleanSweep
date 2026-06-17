using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private CancellationTokenSource? _cts;

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    /// <summary>Phase 3 capabilities, each its own section.</summary>
    public DuplicatesViewModel Duplicates { get; }
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
    // 0 Smart Scan · 1 Large Files · 2 Duplicates · 3 Apps · 4 Startup · 5 Memory · 6 Settings
    [ObservableProperty] private int _selectedSectionIndex;

    public bool IsSmartScanSection => SelectedSectionIndex == 0;
    public bool IsLargeFilesSection => SelectedSectionIndex == 1;
    public bool IsDuplicatesSection => SelectedSectionIndex == 2;
    public bool IsAppsSection => SelectedSectionIndex == 3;
    public bool IsStartupSection => SelectedSectionIndex == 4;
    public bool IsMemorySection => SelectedSectionIndex == 5;
    public bool IsSettingsSection => SelectedSectionIndex == 6;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSmartScanSection));
        OnPropertyChanged(nameof(IsLargeFilesSection));
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

    public MainWindowViewModel() : this(new DialogService()) { }

    public MainWindowViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Duplicates = new DuplicatesViewModel(_engine, PlatformPaths.Current, dialogs);
        Apps = new AppsViewModel(AppInventory.Create(), dialogs);
        Startup = new StartupViewModel(StartupManager.Create(), dialogs);
        Settings = new SettingsViewModel(_explainer, new SettingsStore());
        RefreshMemory();
    }

    partial void OnTotalReclaimableChanged(long value) => OnPropertyChanged(nameof(TotalReclaimableText));
    partial void OnSelectedBytesChanged(long value) => OnPropertyChanged(nameof(SelectedText));

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
            var results = await _engine.ScanAsync(progress, _cts.Token);
            foreach (var result in results)
            {
                if (result.Count == 0) continue;
                var category = new CategoryViewModel(result, _explainer);
                category.PropertyChanged += OnCategoryChanged;
                Categories.Add(category);
            }
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
