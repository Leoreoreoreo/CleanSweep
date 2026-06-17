using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.Core;
using CleanSweep.Core.Cleaning;
using CleanSweep.Core.Duplicates;
using CleanSweep.Core.Models;
using CleanSweep.Core.Platform;
using CleanSweep.Services;

namespace CleanSweep.ViewModels;

public partial class DuplicatesViewModel : ViewModelBase
{
    private readonly ScanEngine _engine;
    private readonly IPlatformPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly IDuplicateFinder _finder = new DuplicateFinder();
    private CancellationTokenSource? _cts;

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Scan your dev & user folders to find identical files you can safely remove.";
    [ObservableProperty] private long _selectedBytes;

    public string SelectedText => ByteSize.Human(SelectedBytes);
    public bool HasGroups => Groups.Count > 0;

    public DuplicatesViewModel(ScanEngine engine, IPlatformPaths paths, IDialogService dialogs)
    {
        _engine = engine;
        _paths = paths;
        _dialogs = dialogs;
    }

    partial void OnSelectedBytesChanged(long value) => OnPropertyChanged(nameof(SelectedText));

    [RelayCommand]
    private async Task Scan()
    {
        if (IsBusy) return;
        IsBusy = true;
        Groups.Clear();
        SelectedBytes = 0;
        OnPropertyChanged(nameof(HasGroups));
        _cts = new CancellationTokenSource();

        try
        {
            StatusText = "Scanning for duplicates…";
            var progress = new Progress<string>(s => StatusText = s);
            var roots = _paths.DevSearchRoots.Append(Path.Combine(_paths.HomeDirectory, "Downloads"));
            var groups = await _finder.FindAsync(roots, new DuplicateScanOptions(), progress, _cts.Token);

            foreach (var g in groups)
            {
                var gvm = new DuplicateGroupViewModel(g);
                gvm.SelectionChanged += OnGroupSelectionChanged;
                Groups.Add(gvm);
            }
            OnPropertyChanged(nameof(HasGroups));
            StatusText = groups.Count > 0
                ? $"Found {groups.Count} duplicate group(s) · up to {ByteSize.Human(groups.Sum(g => g.ReclaimableBytes))} reclaimable."
                : "No duplicates found. ✨";
        }
        catch (OperationCanceledException) { StatusText = "Duplicate scan cancelled."; }
        catch (Exception ex) { StatusText = $"Scan failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task DeleteSelected()
    {
        if (IsBusy) return;

        var toDelete = new List<CleanItem>();
        foreach (var group in Groups)
        {
            var selected = group.Files.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0) continue;
            // Keep-one safety net: never remove every copy in a group.
            if (selected.Count >= group.Files.Count) selected = selected.Skip(1).ToList();
            toDelete.AddRange(selected.Select(f => f.ToCleanItem()));
        }

        if (toDelete.Count == 0)
        {
            StatusText = "Select the duplicate copies to remove — one copy in each group is always kept.";
            return;
        }

        long bytes = toDelete.Sum(i => i.SizeBytes);
        bool ok = await _dialogs.ConfirmAsync(
            "Delete duplicates?",
            $"Permanently delete {toDelete.Count} duplicate file(s), freeing about {ByteSize.Human(bytes)}? One copy in each group is kept.",
            "Delete", destructive: true);
        if (!ok) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            var outcome = await _engine.CleanAsync(toDelete, progress, _cts.Token); // routes through SafeDeleter
            var skipped = outcome.SkippedCount > 0 ? $"  ·  {outcome.SkippedCount} skipped" : "";
            StatusText = $"Removed {outcome.DeletedCount} duplicate(s), freed {ByteSize.Human(outcome.FreedBytes)}{skipped}.";
            await Scan(); // refresh the list
        }
        catch (Exception ex) { StatusText = $"Delete failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void OnGroupSelectionChanged(object? sender, EventArgs e)
        => SelectedBytes = Groups.Sum(g => g.SelectedBytes);
}

public partial class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroup Model { get; }
    public ObservableCollection<DuplicateFileViewModel> Files { get; } = new();

    [ObservableProperty] private bool _isExpanded;

    public event EventHandler? SelectionChanged;

    public DuplicateGroupViewModel(DuplicateGroup model)
    {
        Model = model;
        // Show the oldest copy first as the natural "keep".
        foreach (var f in model.Files.OrderBy(f => f.LastModified ?? DateTime.MaxValue))
        {
            var vm = new DuplicateFileViewModel(f);
            vm.PropertyChanged += OnFileChanged;
            Files.Add(vm);
        }
    }

    public string Header =>
        $"{Files.Count} copies · {ByteSize.Human(Model.FileSizeBytes)} each · up to {ByteSize.Human(Model.ReclaimableBytes)} reclaimable";
    public string Title => Files.Count > 0 ? Files[0].Name : "Duplicate set";
    public long SelectedBytes => Files.Where(f => f.IsSelected).Sum(f => f.SizeBytes);

    /// <summary>Selects every copy except the first (the suggested keep).</summary>
    [RelayCommand]
    private void SelectExtras()
    {
        for (int i = 0; i < Files.Count; i++) Files[i].IsSelected = i != 0;
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateFileViewModel.IsSelected))
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public partial class DuplicateFileViewModel : ObservableObject
{
    public DuplicateFile Model { get; }

    [ObservableProperty] private bool _isSelected;

    public DuplicateFileViewModel(DuplicateFile model) => Model = model;

    public string Name => Model.Name;
    public string Path => Model.Path;
    public long SizeBytes => Model.SizeBytes;
    public string SizeText => ByteSize.Human(Model.SizeBytes);
    public string ModifiedText => Model.LastModified?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";

    public CleanItem ToCleanItem() => new()
    {
        Path = Model.Path,
        DisplayName = Model.Name,
        SizeBytes = Model.SizeBytes,
        Category = CleanCategory.Duplicates,
        IsDirectory = false
    };
}
