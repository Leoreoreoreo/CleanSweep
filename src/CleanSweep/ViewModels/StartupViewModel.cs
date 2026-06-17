using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.Core.Startup;
using CleanSweep.Services;

namespace CleanSweep.ViewModels;

public partial class StartupViewModel : ViewModelBase
{
    private readonly IStartupManager _manager;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText;

    public bool IsSupported => _manager.IsSupported;
    public bool HasItems => Items.Count > 0;

    public StartupViewModel(IStartupManager manager, IDialogService dialogs)
    {
        _manager = manager;
        _dialogs = dialogs;
        _statusText = _manager.IsSupported
            ? "Review what launches at login. Disabling is reversible - prefer it over removing."
            : "Startup management isn't supported on this OS.";
    }

    [RelayCommand]
    private async Task Load()
    {
        if (IsBusy || !_manager.IsSupported) return;
        IsBusy = true;
        Items.Clear();
        OnPropertyChanged(nameof(HasItems));
        _cts = new CancellationTokenSource();
        try
        {
            StatusText = "Reading startup items...";
            var items = await _manager.ListAsync(_cts.Token);
            foreach (var item in items) Items.Add(new StartupItemViewModel(item, this));
            OnPropertyChanged(nameof(HasItems));
            StatusText = $"{Items.Count} startup item(s) found.";
        }
        catch (OperationCanceledException) { StatusText = "Cancelled."; }
        catch (Exception ex) { StatusText = $"Could not list startup items: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public async Task ToggleAsync(StartupItemViewModel item)
    {
        if (IsBusy) return;
        bool enable = !item.IsEnabled;

        // Disabling is reversible; removing a login item is not - confirm that case.
        if (!enable && item.Model.Kind == StartupKind.LoginItem)
        {
            bool ok = await _dialogs.ConfirmAsync(
                $"Remove {item.Name}?",
                $"Login items can't be re-enabled automatically. Remove “{item.Name}” from your login items?",
                "Remove", destructive: true);
            if (!ok) return;
        }

        IsBusy = true;
        try
        {
            var result = await _manager.SetEnabledAsync(item.Model, enable, CancellationToken.None);
            StatusText = result.Message;
            await Load(); // re-read state from the OS
        }
        catch (Exception ex) { StatusText = $"Action failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}

public partial class StartupItemViewModel : ObservableObject
{
    private readonly StartupViewModel _parent;
    public StartupItem Model { get; }

    public StartupItemViewModel(StartupItem model, StartupViewModel parent)
    {
        Model = model;
        _parent = parent;
    }

    public string Name => Model.Name;
    public string Command => Model.Command ?? "";
    public bool IsEnabled => Model.IsEnabled;
    public string StateText => Model.IsEnabled ? "Enabled" : "Disabled";
    public string KindLabel => Model.KindLabel;
    public string ScopeLabel => Model.ScopeLabel;
    public string ToggleLabel => Model.IsEnabled
        ? (Model.Kind == StartupKind.LoginItem ? "Remove" : "Disable")
        : "Enable";

    [RelayCommand]
    private Task Toggle() => _parent.ToggleAsync(this);
}
