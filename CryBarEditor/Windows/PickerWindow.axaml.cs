using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Interactivity;

using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

public partial class PickerWindow : SimpleWindow
{
    readonly IReadOnlyList<string> _allItems;

    string _filter = "";
    string? _selectedItem;

    public string TitleText { get; }
    public string? PickedItem { get; private set; }

    public string Filter
    {
        get => _filter;
        set { _filter = value; OnSelfChanged(); RefreshFiltered(); }
    }

    public string? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            OnSelfChanged();
            if (_confirmBtn != null)
                _confirmBtn.IsEnabled = value != null;
        }
    }

    public string StatusText => _filter.Length == 0
        ? $"{FilteredItems.Count} item{(FilteredItems.Count == 1 ? "" : "s")}"
        : $"{FilteredItems.Count} of {_allItems.Count}";

    public ObservableCollectionExtended<string> FilteredItems { get; } = new();

    public PickerWindow() : this("Picker", System.Array.Empty<string>(), null) { }

    public PickerWindow(string title, IReadOnlyList<string> items, int? preselectIndex)
    {
        TitleText = title;
        _allItems = items;
        InitializeComponent();

        RefreshFiltered();

        if (preselectIndex is int idx && idx >= 0 && idx < items.Count)
        {
            SelectedItem = items[idx];
            _list.ScrollIntoView(items[idx]);
        }
    }

    void RefreshFiltered()
    {
        var prevSelected = _selectedItem;
        FilteredItems.Clear();
        var filter = _filter.Trim();
        if (filter.Length == 0)
        {
            foreach (var s in _allItems) FilteredItems.Add(s);
        }
        else
        {
            foreach (var s in _allItems)
                if (s.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
                    FilteredItems.Add(s);
        }
        OnPropertyChanged(nameof(StatusText));

        // Preserve selection when still in the filtered list, otherwise clear it
        if (prevSelected != null && FilteredItems.Contains(prevSelected))
            SelectedItem = prevSelected;
        else
            SelectedItem = null;
    }

    void ListBox_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (SelectedItem != null)
            OnConfirm(this, new RoutedEventArgs());
    }

    void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is string s)
        {
            PickedItem = s;
            Close();
        }
    }

    void OnCancel(object? sender, RoutedEventArgs e)
    {
        PickedItem = null;
        Close();
    }
}
