using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.Core.Apps;
using CleanSweep.Services;

namespace CleanSweep.ViewModels;

public partial class AppsViewModel : ViewModelBase
{
    private readonly IAppInventory _inventory;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<AppViewModel> Apps { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText;

    public bool IsSupported => _inventory.IsSupported;

    public AppsViewModel(IAppInventory inventory, IDialogService dialogs)
    {
        _inventory = inventory;
        _dialogs = dialogs;
        _statusText = _inventory.IsSupported
            ? "List installed applications, then uninstall the ones you no longer need."
            : "App uninstall isn't supported on this OS.";
    }

    public bool HasApps => Apps.Count > 0;

    [RelayCommand]
    private async Task Load()
    {
        if (IsBusy || !_inventory.IsSupported) return;
        IsBusy = true;
        Apps.Clear();
        OnPropertyChanged(nameof(HasApps));
        _cts = new CancellationTokenSource();
        try
        {
            StatusText = "Reading installed applications…";
            var apps = await _inventory.ListAsync(_cts.Token);
            foreach (var app in apps) Apps.Add(new AppViewModel(app, this));
            OnPropertyChanged(nameof(HasApps));
            StatusText = $"{Apps.Count} application(s) found.";
        }
        catch (OperationCanceledException) { StatusText = "Cancelled."; }
        catch (Exception ex) { StatusText = $"Could not list applications: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public async Task UninstallAsync(AppViewModel app)
    {
        if (IsBusy) return;
        bool ok = await _dialogs.ConfirmAsync(
            $"Uninstall {app.Name}?",
            OperatingSystem.IsMacOS()
                ? $"Move {app.Name} and its support files to the Trash? You can restore them from the Trash until you empty it."
                : $"Launch the uninstaller for {app.Name}? This will start removing the app from your system.",
            "Uninstall", destructive: true);
        if (!ok) return;

        IsBusy = true;
        try
        {
            var result = await _inventory.UninstallAsync(app.Model, CancellationToken.None);
            StatusText = result.Message;
            if (result.Succeeded && OperatingSystem.IsMacOS())
            {
                Apps.Remove(app);
                OnPropertyChanged(nameof(HasApps));
            }
        }
        catch (Exception ex) { StatusText = $"Uninstall failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}

public partial class AppViewModel : ObservableObject
{
    private readonly AppsViewModel _parent;
    public InstalledApp Model { get; }

    public AppViewModel(InstalledApp model, AppsViewModel parent)
    {
        Model = model;
        _parent = parent;
    }

    public string Name => Model.Name;
    public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? "Unknown publisher" : Model.Publisher!;
    public string Version => string.IsNullOrWhiteSpace(Model.Version) ? "" : $"v{Model.Version}";
    public string SizeText => Model.SizeText;

    [RelayCommand]
    private Task Uninstall() => _parent.UninstallAsync(this);
}
