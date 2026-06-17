using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.AI;
using CleanSweep.Services;

namespace CleanSweep.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AiItemExplainer _explainer;
    private readonly SettingsStore _store;
    private readonly IDialogService _dialogs;

    public string[] Providers { get; } = AiProviders.All.Select(p => p.DisplayName).ToArray();
    public string[] Themes { get; } = { "System", "Light", "Dark" };
    public string[] AutoScanOptions { get; } = { "Off", "Every 6 hours", "Every 24 hours" };

    [ObservableProperty] private int _selectedProviderIndex;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _themeIndex; // 0 System · 1 Light · 2 Dark
    [ObservableProperty] private int _autoScanIndex; // 0 Off · 1 every 6h · 2 every 24h
    [ObservableProperty] private string? _selectedExclusion;

    /// <summary>Folders the scans will skip; edited here, read by the scan view models.</summary>
    public ObservableCollection<string> Exclusions { get; } = new();

    /// <summary>Raised when the auto-scan cadence changes, so the shell can re-arm its timer.</summary>
    public event EventHandler? AutoScanChanged;

    /// <summary>Snapshot of the excluded folders for a scan.</summary>
    public IReadOnlyList<string> ExcludedPaths => Exclusions.ToList();

    /// <summary>The chosen auto-scan period, or null when off.</summary>
    public TimeSpan? AutoScanInterval => AutoScanIndex switch
    {
        1 => TimeSpan.FromHours(6),
        2 => TimeSpan.FromHours(24),
        _ => null
    };

    public string SettingsPath => _store.SettingsPath;
    public bool IsAiEnabled => _explainer.IsAiEnabled;
    public bool HasExclusions => Exclusions.Count > 0;

    private AiProviderInfo Current => AiProviders.All[SelectedProviderIndex];
    public string KeyLabel => $"{Current.DisplayName} API key";
    public string ModelHint => string.IsNullOrEmpty(Current.DefaultModel)
        ? $"e.g. {Current.ModelHint}"
        : $"default: {Current.DefaultModel} - e.g. {Current.ModelHint}";
    public bool ShowBaseUrl => Current.BaseUrlEditable;

    public SettingsViewModel(AiItemExplainer explainer, SettingsStore store, IDialogService dialogs)
    {
        _explainer = explainer;
        _store = store;
        _dialogs = dialogs;

        var s = store.Load();
        _selectedProviderIndex = IndexOf(AiProviders.Parse(s.Provider));
        _apiKey = s.ApiKey ?? "";
        _model = s.Model ?? "";
        _baseUrl = s.BaseUrl ?? "";
        _themeIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _autoScanIndex = s.AutoScan switch { "6h" => 1, "24h" => 2, _ => 0 };
        foreach (var path in s.Exclusions.Where(p => !string.IsNullOrWhiteSpace(p)))
            Exclusions.Add(path);
        Exclusions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasExclusions));

        ApplyTheme();
        if (!string.IsNullOrWhiteSpace(s.ApiKey)) _explainer.Configure(BuildSettings());
        UpdateStatus();
    }

    partial void OnSelectedProviderIndexChanged(int value)
    {
        OnPropertyChanged(nameof(KeyLabel));
        OnPropertyChanged(nameof(ModelHint));
        OnPropertyChanged(nameof(ShowBaseUrl));
    }

    partial void OnThemeIndexChanged(int value)
    {
        ApplyTheme();
        Persist();
    }

    partial void OnAutoScanIndexChanged(int value)
    {
        Persist();
        AutoScanChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task AddExclusion()
    {
        var path = await _dialogs.PickFolderAsync("Choose a folder to exclude from scans");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Exclusions.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
        {
            Exclusions.Add(path);
            Persist();
        }
    }

    [RelayCommand]
    private void RemoveExclusion(string? path)
    {
        path ??= SelectedExclusion;
        if (path is null) return;
        if (Exclusions.Remove(path)) Persist();
    }

    [RelayCommand]
    private void Save()
    {
        Persist();
        _explainer.Configure(BuildSettings());
        OnPropertyChanged(nameof(IsAiEnabled));
        StatusText = _explainer.IsAiEnabled
            ? $"Saved - {Current.DisplayName} explanations are on."
            : "Saved - enter an API key above to turn on AI explanations.";
    }

    [RelayCommand]
    private void ClearKey()
    {
        ApiKey = "";
        Save();
    }

    private AiSettings BuildSettings() =>
        new(Current.Provider, ApiKey, Model, string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl);

    private void Persist() => _store.Save(new AppSettings
    {
        Provider = Current.Provider.ToString(),
        ApiKey = ApiKey,
        Model = Model,
        BaseUrl = BaseUrl,
        Theme = ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" },
        AutoScan = AutoScanIndex switch { 1 => "6h", 2 => "24h", _ => "Off" },
        Exclusions = Exclusions.ToList()
    });

    private void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ThemeIndex switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
    }

    private void UpdateStatus() => StatusText = _explainer.IsAiEnabled
        ? "AI explanations are on."
        : "AI explanations are off - choose a provider and paste your API key below.";

    private static int IndexOf(AiProvider provider)
    {
        for (int i = 0; i < AiProviders.All.Count; i++)
            if (AiProviders.All[i].Provider == provider) return i;
        return 0;
    }
}
