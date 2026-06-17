using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CleanSweep.Core;
using CleanSweep.Core.AI;
using CleanSweep.Core.Models;

namespace CleanSweep.ViewModels;

public partial class CategoryViewModel : ObservableObject
{
    public CleanCategory Category { get; }
    public ObservableCollection<CleanItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private long _selectedBytes;
    [ObservableProperty] private bool? _allSelected = false;

    private bool _suppress;

    public CategoryViewModel(CategoryResult result, IItemExplainer explainer)
    {
        Category = result.Category;
        foreach (var item in result.Items)
        {
            var vm = new CleanItemViewModel(item, explainer);
            vm.PropertyChanged += OnItemPropertyChanged;
            Items.Add(vm);
        }
        Recompute();
    }

    public string Name => Category.DisplayName();
    public string Glyph => Category.Glyph();
    public string Description => Category.Description();
    public int Count => Items.Count;
    public string Summary => $"{Count} item{(Count == 1 ? "" : "s")}  ·  {ByteSize.Human(TotalBytes)}";
    public string SelectedText => ByteSize.Human(SelectedBytes);

    public IEnumerable<CleanItem> SelectedModels => Items.Where(i => i.IsSelected).Select(i => i.Model);

    partial void OnAllSelectedChanged(bool? value)
    {
        if (_suppress || value is null) return;
        foreach (var item in Items) item.IsSelected = value.Value;
    }

    partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(Summary));
    partial void OnSelectedBytesChanged(long value) => OnPropertyChanged(nameof(SelectedText));

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanItemViewModel.IsSelected)) Recompute();
    }

    private void Recompute()
    {
        TotalBytes = Items.Sum(i => i.SizeBytes);
        SelectedBytes = Items.Where(i => i.IsSelected).Sum(i => i.SizeBytes);

        _suppress = true;
        int selected = Items.Count(i => i.IsSelected);
        AllSelected = Items.Count == 0 ? false
            : selected == 0 ? false
            : selected == Items.Count ? true
            : null;
        _suppress = false;
    }
}
