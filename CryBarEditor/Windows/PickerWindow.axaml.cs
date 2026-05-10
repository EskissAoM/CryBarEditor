using System.Collections.Generic;
using System.Collections.ObjectModel;

using Avalonia.Controls;
using Avalonia.Interactivity;

using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

public partial class PickerWindow : SimpleWindow
{
    readonly IReadOnlyList<string> _allItems;
    readonly ObservableCollection<string> _filtered = new();

    public string TitleText { get; }
    public string? PickedItem { get; private set; }

    public PickerWindow() : this("Picker", System.Array.Empty<string>(), null) { }

    public PickerWindow(string title, IReadOnlyList<string> items, int? preselectIndex)
    {
        TitleText = title;
        _allItems = items;
        InitializeComponent();

        foreach (var s in items) _filtered.Add(s);
        _list.ItemsSource = _filtered;

        if (preselectIndex is int idx && idx >= 0 && idx < items.Count)
        {
            _list.SelectedIndex = idx;
            _list.ScrollIntoView(items[idx]);
        }

        _filterBox.TextChanged += (_, _) => RefreshFilter();
        _filterBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && _filtered.Count == 1)
            {
                _list.SelectedIndex = 0;
                OnConfirm(this, new RoutedEventArgs());
            }
        };
        _list.SelectionChanged += (_, _) =>
            _confirmBtn.IsEnabled = _list.SelectedItem is string;
    }

    void RefreshFilter()
    {
        var q = _filterBox.Text ?? "";
        _filtered.Clear();
        if (string.IsNullOrEmpty(q))
        {
            foreach (var s in _allItems) _filtered.Add(s);
        }
        else
        {
            foreach (var s in _allItems)
                if (s.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                    _filtered.Add(s);
        }
    }

    void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (_list.SelectedItem is string s)
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
