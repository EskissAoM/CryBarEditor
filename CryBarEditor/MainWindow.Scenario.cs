using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CryBar.Scenario;
using CryBarEditor.Classes;
using CryBarEditor.Controls;

namespace CryBarEditor;

public partial class MainWindow
{
    ScenarioPreviewData? _scenarioData;
    GlScenarioPreviewControl? _scenarioGl;
    string? _manualTextureBarPath;
    ScenarioFile? _pendingScenario3D;

    void ShowScenarioPreview(ScenarioFile scenario)
    {
        HideTmmPreview();
        HideScenarioPreview();

        _flatPreview.IsVisible = false;
        _scenarioRoot.IsVisible = true;

        // Reparent the main editor into the XML tab so the scenario view reuses
        // the existing TextMate grammar, folding manager, and large-doc cache.
        if (_txtEditor.Parent is Panel panel)
            panel.Children.Remove(_txtEditor);
        _scenarioXmlHost.Content = _txtEditor;
        _txtEditor.IsVisible = true;
        _ = SetEditorText(".xml", ScenarioFile.StripBinaryForPreview(scenario.ToXml()));

        // Defer 3D build/upload until the user actually opens the 3D View tab.
        _pendingScenario3D = scenario;
        if (_scenarioTabStrip.SelectedIndex == 1) FlushPendingScenario3D();
    }

    void FlushPendingScenario3D()
    {
        var scenario = _pendingScenario3D;
        if (scenario is null) return;
        _pendingScenario3D = null;

        var data = ScenarioPreviewData.TryBuild(scenario);
        if (data is null)
        {
            _scenarioErrorPanel.IsVisible = true;
            _scenarioErrorText.Text = "Could not parse scenario terrain.";
            _scenarioCanvasContainer.IsVisible = false;
            return;
        }
        _scenarioData = data;

        _scenarioGl ??= CreateScenarioGl();
        _scenarioCanvasContainer.Child = _scenarioGl;
        _scenarioCanvasContainer.IsVisible = true;
        _scenarioErrorPanel.IsVisible = false;
        _scenarioInspector.SetCursor(null, null);

        _scenarioInspector.ClearSelectionRequested -= OnInspectorClearSelection;
        _scenarioInspector.ClearSelectionRequested += OnInspectorClearSelection;

        _scenarioGl.SetScenario(data);

        data.Selection.Changed += () =>
            Dispatcher.UIThread.Post(() => _scenarioInspector.UpdateSelection(_scenarioData));
        _scenarioInspector.UpdateSelection(data);

        _ = LoadScenarioTexturesAsync(data, data.Cancellation.Token);
    }

    void OnInspectorClearSelection() => _scenarioData?.Selection.Clear();

    GlScenarioPreviewControl CreateScenarioGl()
    {
        var gl = new GlScenarioPreviewControl();
        gl.CursorHit += hit =>
            Dispatcher.UIThread.Post(() => _scenarioInspector.SetCursor(hit, _scenarioData));
        gl.LeftClicked += (hit, ctrl) =>
        {
            if (_scenarioData is null) return;
            ScenarioSelectionInput.OnLeftClick(_scenarioData.Selection, hit, ctrl);
        };
        gl.RightClicked += (hit, ctrl) =>
        {
            if (_scenarioData is null) return;
            ScenarioSelectionInput.OnRightClick(_scenarioData.Selection, hit, ctrl);
        };
        gl.ErrorChanged += msg =>
            Dispatcher.UIThread.Post(() =>
            {
                _scenarioErrorText.Text = msg ?? "";
                _scenarioErrorPanel.IsVisible = msg is not null;
            });
        gl.ShowEntities = _showScenarioEntitiesCheckbox.IsChecked == true;
        gl.ShowWater = _showScenarioWaterCheckbox.IsChecked == true;
        return gl;
    }

    void ScenarioTab_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ToggleTabPanels(_scenarioTabStrip, _scenarioXmlHost, _scenario3dPanel);
        if (_scenarioTabStrip?.SelectedIndex == 1 && _pendingScenario3D is not null)
            FlushPendingScenario3D();
    }

    void ShowScenarioEntities_Toggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_scenarioGl != null)
            _scenarioGl.ShowEntities = _showScenarioEntitiesCheckbox.IsChecked == true;
        SaveConfiguration();
    }

    void ShowScenarioWater_Toggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_scenarioGl != null)
            _scenarioGl.ShowWater = _showScenarioWaterCheckbox.IsChecked == true;
        SaveConfiguration();
    }

    void UpdateScenarioProgress(ScenarioTextureLoader.LoadProgress p)
    {
        _scenarioProgressText.Text =
            $"Resolving {p.Resolved}/{p.Total} - Decoding {p.Decoded}/{p.Total} - Uploading {p.Uploaded}/{p.Total}";
        _scenarioProgressBar.Value = (p.Resolved + p.Decoded + p.Uploaded) / (3.0 * Math.Max(1, p.Total));
    }

    async Task LoadScenarioTexturesAsync(ScenarioPreviewData data, CancellationToken ct)
    {
        if (_fileIndex is null || _scenarioGl is null) return;

        Dispatcher.UIThread.Post(() => _scenarioProgressOverlay.IsVisible = true);

        var manualBarPath = _manualTextureBarPath;
        ManualTextureBar? manualBar = null;
        if (manualBarPath is not null)
        {
            manualBar = await Task.Run(() => ManualTextureBar.TryOpen(manualBarPath), ct);
            if (manualBar is null)
            {
                Dispatcher.UIThread.Post(() =>
                    _scenarioInspector.SetManualBarStatus(manualBarPath, loadFailed: true));
                _manualTextureBarPath = null;
                manualBarPath = null;
            }
        }

        try
        {
            var resolver = new ScenarioTextureLoader.NameResolver(
                _fileIndex,
                ReadFromIndexEntryPooledAsync,
                manualBar is not null ? manualBar.ResolveTextureAsync : null);

            await ScenarioTextureLoader.LoadAllAsync(
                data,
                resolver,
                (sliceIdx, rgba) => _scenarioGl.UploadSliceAsync(sliceIdx, rgba),
                p => Dispatcher.UIThread.Post(() => UpdateScenarioProgress(p)),
                ct);

            Dispatcher.UIThread.Post(() =>
            {
                _scenarioInspector.UpdateAfterLoad(data);
                if (manualBarPath is not null)
                    _scenarioInspector.SetManualBarStatus(manualBarPath, loadFailed: false);
            });
        }
        catch (OperationCanceledException) { /* expected on scenario change */ }
        finally
        {
            manualBar?.Dispose();
            Dispatcher.UIThread.Post(() => _scenarioProgressOverlay.IsVisible = false);
        }
    }

    async void SelectManualTextureBarClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var picker = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select fallback ArtTerrainTextures.bar",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("BAR archive") { Patterns = ["*.bar"] },
            ],
        });
        if (picker.Count == 0) return;
        var local = picker[0].Path.LocalPath;
        if (string.IsNullOrEmpty(local)) return;

        _manualTextureBarPath = local;

        if (_scenarioData is { } existing)
            _ = LoadScenarioTexturesAsync(existing, existing.Cancellation.Token);
        else if (_pendingScenario3D is not null && _scenarioTabStrip.SelectedIndex == 1)
            FlushPendingScenario3D();
    }

    void HideScenarioPreview()
    {
        _pendingScenario3D = null;

        // Dispose() cancels the data's CTS, which propagates to the in-flight load.
        _scenarioData?.Dispose();
        _scenarioData = null;

        if (_scenarioRoot.IsVisible)
        {
            _scenarioRoot.IsVisible = false;
            _flatPreview.IsVisible = true;

            // Move the editor back to the flat preview panel so other dispatchers can use it.
            if (ReferenceEquals(_scenarioXmlHost.Content, _txtEditor))
            {
                _scenarioXmlHost.Content = null;
                if (!_flatPreview.Children.Contains(_txtEditor))
                    _flatPreview.Children.Add(_txtEditor);
            }
        }
        _scenarioProgressOverlay.IsVisible = false;
        _scenarioErrorPanel.IsVisible = false;

        if (_scenarioGl is not null)
            _scenarioGl.SetScenario(null);
    }
}
