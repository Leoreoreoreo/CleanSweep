using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.AI;
using CleanSweep.Services;

namespace CleanSweep.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AnthropicItemExplainer _explainer;
    private readonly SettingsStore _store;

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _statusText = "";

    public string SettingsPath => _store.SettingsPath;
    public bool IsAiEnabled => _explainer.IsAiEnabled;

    public SettingsViewModel(AnthropicItemExplainer explainer, SettingsStore store)
    {
        _explainer = explainer;
        _store = store;

        var saved = store.Load();
        _apiKey = saved.ApiKey ?? "";
        _model = saved.Model ?? "";

        // A saved key/model overrides the env-var default the factory started with.
        if (!string.IsNullOrWhiteSpace(saved.ApiKey) || !string.IsNullOrWhiteSpace(saved.Model))
            _explainer.Configure(saved.ApiKey, string.IsNullOrWhiteSpace(saved.Model) ? null : saved.Model);

        UpdateStatus();
    }

    [RelayCommand]
    private void Save()
    {
        _store.Save(new AppSettings { ApiKey = ApiKey, Model = Model });
        _explainer.Configure(ApiKey, string.IsNullOrWhiteSpace(Model) ? null : Model);
        OnPropertyChanged(nameof(IsAiEnabled));
        StatusText = _explainer.IsAiEnabled
            ? "Saved — AI explanations are on."
            : "Saved — enter a key above to turn on AI explanations.";
    }

    [RelayCommand]
    private void ClearKey()
    {
        ApiKey = "";
        Model = "";
        Save();
    }

    private void UpdateStatus() => StatusText = _explainer.IsAiEnabled
        ? "AI explanations are on."
        : "AI explanations are off — paste your Anthropic API key below to enable them.";
}
