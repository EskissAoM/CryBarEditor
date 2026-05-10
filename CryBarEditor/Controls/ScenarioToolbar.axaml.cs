using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CryBar.Scenario.Editor;

namespace CryBarEditor.Controls;

public partial class ScenarioToolbar : UserControl
{
    ScenarioEditor? _editor;
    string? _sourcePath;

    public bool IsDirty => _editor?.IsDirty ?? false;

    public event Action? SaveRequested;
    public event Action? SaveAsRequested;
    public event Action? DiscardRequested;

    public ScenarioToolbar()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>
    /// Wires the toolbar to a scenario editor. Pass null to detach (no scenario
    /// loaded). The optional sourcePath shows up in the path label when the
    /// editor has not yet been saved (i.e. SavePath is null).
    /// </summary>
    public void Bind(ScenarioEditor? editor, string? sourcePath)
    {
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        _editor = editor;
        _sourcePath = sourcePath;
        if (_editor is not null) _editor.Changed += OnEditorChanged;
        Refresh();
    }

    void OnEditorChanged() => Refresh();

    void Refresh()
    {
        var hasEditor = _editor is not null;
        var dirty = hasEditor && _editor!.IsDirty;

        _dirtyIndicator.IsVisible = dirty;
        _saveBtn.IsEnabled = dirty;
        _saveAsBtn.IsEnabled = hasEditor;
        _discardBtn.IsEnabled = dirty;
        _pathLabel.Text = hasEditor
            ? Shorten(_editor!.SavePath ?? _sourcePath ?? "")
            : "";
    }

    // Trim a path to its last two segments so the label fits next to the
    // buttons; full path remains as the underlying string for tooltips/copy.
    static string Shorten(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var parts = path.Replace('\\', '/').TrimEnd('/').Split('/');
        return parts.Length <= 2 ? path : ".../" + string.Join('/', parts[^2..]);
    }

    void OnSaveClick(object? sender, RoutedEventArgs e) => SaveRequested?.Invoke();
    void OnSaveAsClick(object? sender, RoutedEventArgs e) => SaveAsRequested?.Invoke();
    void OnDiscardClick(object? sender, RoutedEventArgs e) => DiscardRequested?.Invoke();
}
