using System.Linq;
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

    public string[] Providers { get; } = AiProviders.All.Select(p => p.DisplayName).ToArray();
    public string[] Themes { get; } = { "System", "Light", "Dark" };

    [ObservableProperty] private int _selectedProviderIndex;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _themeIndex; // 0 System · 1 Light · 2 Dark

    public string SettingsPath => _store.SettingsPath;
    public bool IsAiEnabled => _explainer.IsAiEnabled;

    private AiProviderInfo Current => AiProviders.All[SelectedProviderIndex];
    public string KeyLabel => $"{Current.DisplayName} API key";
    public string ModelHint => string.IsNullOrEmpty(Current.DefaultModel)
        ? $"e.g. {Current.ModelHint}"
        : $"default: {Current.DefaultModel} — e.g. {Current.ModelHint}";
    public bool ShowBaseUrl => Current.BaseUrlEditable;

    public SettingsViewModel(AiItemExplainer explainer, SettingsStore store)
    {
        _explainer = explainer;
        _store = store;

        var s = store.Load();
        _selectedProviderIndex = IndexOf(AiProviders.Parse(s.Provider));
        _apiKey = s.ApiKey ?? "";
        _model = s.Model ?? "";
        _baseUrl = s.BaseUrl ?? "";
        _themeIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

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

    [RelayCommand]
    private void Save()
    {
        Persist();
        _explainer.Configure(BuildSettings());
        OnPropertyChanged(nameof(IsAiEnabled));
        StatusText = _explainer.IsAiEnabled
            ? $"Saved — {Current.DisplayName} explanations are on."
            : "Saved — enter an API key above to turn on AI explanations.";
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
        Theme = ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" }
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
        : "AI explanations are off — choose a provider and paste your API key below.";

    private static int IndexOf(AiProvider provider)
    {
        for (int i = 0; i < AiProviders.All.Count; i++)
            if (AiProviders.All[i].Provider == provider) return i;
        return 0;
    }
}
