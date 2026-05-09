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
        _scenarioInspector.SetEntity(null);

        _scenarioGl.SetScenario(data);

        _ = LoadScenarioTexturesAsync(data, data.Cancellation.Token);
    }

    GlScenarioPreviewControl CreateScenarioGl()
    {
        var gl = new GlScenarioPreviewControl();
        gl.CursorHit += hit =>
            Dispatcher.UIThread.Post(() => _scenarioInspector.SetCursor(hit, _scenarioData));
        gl.EntitySelected += entity =>
            Dispatcher.UIThread.Post(() => _scenarioInspector.SetEntity(entity));
        gl.ErrorChanged += msg =>
            Dispatcher.UIThread.Post(() =>
            {
                _scenarioErrorText.Text = msg ?? "";
                _scenarioErrorPanel.IsVisible = msg is not null;
            });
        return gl;
    }

    void ScenarioTab_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        => ToggleTabPanels(_scenarioTabStrip, _scenarioXmlHost, _scenario3dPanel);

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

        ManualTextureBar? manualBar = null;
        if (_manualTextureBarPath is not null)
        {
            manualBar = ManualTextureBar.TryOpen(_manualTextureBarPath);
            if (manualBar is null)
            {
                // Capture before clearing so the dispatcher post sees the path.
                var failedPath = _manualTextureBarPath;
                Dispatcher.UIThread.Post(() =>
                    _scenarioInspector.SetManualBarStatus(failedPath, loadFailed: true));
                _manualTextureBarPath = null;
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

            var pathSnapshot = _manualTextureBarPath;
            Dispatcher.UIThread.Post(() => _scenarioInspector.UpdateAfterLoad(data, pathSnapshot));
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

        if (_scenarioData?.Scenario is { } scn)
            ShowScenarioPreview(scn);
    }

    void HideScenarioPreview()
    {
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
