using CommunityToolkit.Mvvm.ComponentModel;
using CleanSweep.Core;
using CleanSweep.Core.Models;

namespace CleanSweep.ViewModels;

public partial class CleanItemViewModel : ObservableObject
{
    public CleanItem Model { get; }

    [ObservableProperty]
    private bool _isSelected;

    public CleanItemViewModel(CleanItem model)
    {
        Model = model;
        _isSelected = model.DefaultSelected;
    }

    public string DisplayName => Model.DisplayName;
    public string Path => Model.Path;
    public long SizeBytes => Model.SizeBytes;
    public string SizeText => ByteSize.Human(Model.SizeBytes);
}
