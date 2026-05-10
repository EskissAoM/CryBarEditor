using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

public sealed class PickerItem
{
    public required string Display { get; init; }
    public string? Group { get; init; }
    public IBrush? GroupBrush { get; init; }
    public bool ShowGroup => !string.IsNullOrEmpty(Group);
}

public partial class PickerWindow : SimpleWindow
{
    // Muted-but-distinct hues that read well on the dark #171717 listbox background.
    static readonly IBrush[] GroupPalette =
    {
        new SolidColorBrush(Color.FromRgb(0x7f, 0xbf, 0xff)), // light blue
        new SolidColorBrush(Color.FromRgb(0x7f, 0xff, 0x9c)), // light green
        new SolidColorBrush(Color.FromRgb(0xff, 0xb3, 0x7f)), // peach
        new SolidColorBrush(Color.FromRgb(0xff, 0x9c, 0xc8)), // pink
        new SolidColorBrush(Color.FromRgb(0xc8, 0x9c, 0xff)), // lavender
        new SolidColorBrush(Color.FromRgb(0xff, 0xe0, 0x7f)), // light yellow
        new SolidColorBrush(Color.FromRgb(0x7f, 0xe0, 0xc8)), // mint
        new SolidColorBrush(Color.FromRgb(0xe0, 0xc8, 0x9c)), // tan
        new SolidColorBrush(Color.FromRgb(0xff, 0x9c, 0x9c)), // salmon
        new SolidColorBrush(Color.FromRgb(0x9c, 0xff, 0xff)), // pale cyan
    };

    readonly IReadOnlyList<PickerItem> _allItems;

    string _filter = "";
    PickerItem? _selectedItem;

    public string TitleText { get; }
    public PickerItem? Picked { get; private set; }
    public string? PickedItem => Picked?.Display;
    public int? PickedIndex { get; private set; }

    public string Filter
    {
        get => _filter;
        set { _filter = value; OnSelfChanged(); RefreshFiltered(); }
    }

    public PickerItem? SelectedItem
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

    public ObservableCollectionExtended<PickerItem> FilteredItems { get; } = new();

    public PickerWindow() : this("Picker", System.Array.Empty<string>(), null) { }

    public PickerWindow(string title, IReadOnlyList<string> items, int? preselectIndex)
        : this(title,
               items.Select(s => new PickerItem { Display = s, Group = null }).ToList(),
               preselectIndex)
    { }

    public PickerWindow(string title, IReadOnlyList<PickerItem> items, int? preselectIndex)
    {
        TitleText = title;

        // Assign a brush per unique Group in first-appearance order.
        var groupToBrush = new Dictionary<string, IBrush>();
        var enriched = new List<PickerItem>(items.Count);
        foreach (var it in items)
        {
            IBrush? brush = null;
            if (it.Group is not null)
            {
                if (!groupToBrush.TryGetValue(it.Group, out brush))
                {
                    brush = GroupPalette[groupToBrush.Count % GroupPalette.Length];
                    groupToBrush[it.Group] = brush;
                }
            }
            enriched.Add(new PickerItem { Display = it.Display, Group = it.Group, GroupBrush = brush });
        }
        _allItems = enriched;

        InitializeComponent();

        RefreshFiltered();

        if (preselectIndex is int idx && idx >= 0 && idx < _allItems.Count)
        {
            SelectedItem = _allItems[idx];
            _list.ScrollIntoView(_allItems[idx]);
        }
    }

    void RefreshFiltered()
    {
        var prevSelected = _selectedItem;
        FilteredItems.Clear();
        var filter = _filter.Trim();
        if (filter.Length == 0)
        {
            foreach (var it in _allItems) FilteredItems.Add(it);
        }
        else
        {
            foreach (var it in _allItems)
            {
                var combined = it.Group is null ? it.Display : it.Group + "|" + it.Display;
                if (combined.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
                    FilteredItems.Add(it);
            }
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
        if (SelectedItem is PickerItem item)
        {
            Picked = item;
            int i = -1;
            for (int k = 0; k < _allItems.Count; k++)
            {
                if (ReferenceEquals(_allItems[k], item)) { i = k; break; }
            }
            PickedIndex = i >= 0 ? i : null;
            Close();
        }
    }

    void OnCancel(object? sender, RoutedEventArgs e)
    {
        Picked = null;
        PickedIndex = null;
        Close();
    }
}
