using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanSweep.Core;
using CleanSweep.Core.AI;
using CleanSweep.Core.Models;

namespace CleanSweep.ViewModels;

public partial class CleanItemViewModel : ObservableObject
{
    private readonly IItemExplainer _explainer;

    public CleanItem Model { get; }

    [ObservableProperty] private bool _isSelected;

    // "What is this?" AI explainer state.
    [ObservableProperty] private bool _isExplanationOpen;
    [ObservableProperty] private bool _isExplaining;
    [ObservableProperty] private ItemExplanation? _explanation;

    public CleanItemViewModel(CleanItem model, IItemExplainer explainer)
    {
        Model = model;
        _explainer = explainer;
        _isSelected = model.DefaultSelected;
    }

    public string DisplayName => Model.DisplayName;
    public string Path => Model.Path;
    public long SizeBytes => Model.SizeBytes;
    public string SizeText => ByteSize.Human(Model.SizeBytes);

    public bool HasExplanation => Explanation is not null;
    public bool ShowOfflineHint => Explanation is { FromAi: false } && !_explainer.IsAiEnabled;

    public string RiskText => Explanation?.Risk switch
    {
        RiskLevel.Safe => "Safe to delete",
        RiskLevel.Caution => "Caution",
        RiskLevel.Risky => "Risky",
        RiskLevel.Unknown => "Unknown",
        _ => ""
    };

    public IBrush RiskBrush => Explanation?.Risk switch
    {
        RiskLevel.Safe => new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x50)),    // green
        RiskLevel.Caution => new SolidColorBrush(Color.FromRgb(0xE0, 0xA3, 0x2E)), // amber
        RiskLevel.Risky => new SolidColorBrush(Color.FromRgb(0xE5, 0x55, 0x4D)),   // red
        _ => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))                  // grey
    };

    [RelayCommand]
    private async Task Explain()
    {
        // Toggle closed if already showing.
        if (IsExplanationOpen && HasExplanation) { IsExplanationOpen = false; return; }
        IsExplanationOpen = true;
        if (HasExplanation || IsExplaining) return; // already have it / in flight

        IsExplaining = true;
        try
        {
            Explanation = await _explainer.ExplainAsync(Model, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Explanation = new ItemExplanation("Couldn't load an explanation.", RiskLevel.Unknown,
                ex.Message, "Try again in a moment.") { Source = "offline" };
        }
        finally
        {
            IsExplaining = false;
        }
    }

    partial void OnExplanationChanged(ItemExplanation? value)
    {
        OnPropertyChanged(nameof(HasExplanation));
        OnPropertyChanged(nameof(ShowOfflineHint));
        OnPropertyChanged(nameof(RiskText));
        OnPropertyChanged(nameof(RiskBrush));
    }
}
