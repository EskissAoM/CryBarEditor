using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CryBar.Bar;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
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

        // Editor + toolbar + drag-commit wiring (Task 24). Use the -=/+= pattern
        // because FlushPendingScenario3D may run more than once for a single
        // scenario (e.g. tab toggle); HideScenarioPreview also drops the data
        // so subsequent attaches start from a clean slate.
        data.Editor.Changed += () =>
            Dispatcher.UIThread.Post(() => OnEditorChanged(data));

        _scenarioInspector.ExecuteCommand = cmd => data.Editor.Execute(cmd);

        _scenarioToolbar.Bind(data.Editor, sourcePath: ResolveScenarioSourcePath());
        _scenarioToolbar.SaveRequested    -= OnToolbarSave;
        _scenarioToolbar.SaveAsRequested  -= OnToolbarSaveAs;
        _scenarioToolbar.DiscardRequested -= OnToolbarDiscard;
        _scenarioToolbar.SaveRequested    += OnToolbarSave;
        _scenarioToolbar.SaveAsRequested  += OnToolbarSaveAs;
        _scenarioToolbar.DiscardRequested += OnToolbarDiscard;

        if (_scenarioGl is not null)
        {
            _scenarioGl.MoveCommitted -= OnDragMoveCommitted;
            _scenarioGl.MoveCommitted += OnDragMoveCommitted;
        }

        _ = LoadScenarioTexturesAsync(data, data.Cancellation.Token);
    }

    void OnEditorChanged(ScenarioPreviewData data)
    {
        // The closure that calls us captures `data`, not _scenarioData. After
        // a scenario swap the old data's editor may still fire Changed once
        // before being GC'd; ignore those events so we don't reset the
        // inspector to a disposed scenario.
        if (!ReferenceEquals(data, _scenarioData)) return;

        // Read the hint published by the most recent command. Discard() sets
        // LastChange to null -> RenderHint.None means "rebuild everything";
        // we treat None as a signal to rebuild all four buffers.
        var hint = data.Editor.LastChange?.Hint;
        var effective = hint ?? (RenderHint.TerrainTexture | RenderHint.TerrainGeometry
                                | RenderHint.TerrainWater   | RenderHint.EntityList);

        // EntityList = entity added/removed. Selection may now reference dead
        // ids, so prune them BEFORE the renderer rebuild reads selection state.
        if ((effective & RenderHint.EntityList) != 0)
        {
            var liveIds = new HashSet<uint>();
            foreach (var e in data.Entities) liveIds.Add(e.EntityId);
            List<uint>? toRemove = null;
            foreach (var id in data.Selection.Entities)
            {
                if (!liveIds.Contains(id))
                {
                    toRemove ??= new List<uint>();
                    toRemove.Add(id);
                }
            }
            if (toRemove is not null)
                foreach (var id in toRemove) data.Selection.RemoveEntity(id);
        }

        _scenarioGl?.OnDataMutated(effective);
        _scenarioInspector.UpdateSelection(data);
    }

    void OnDragMoveCommitted(IScenarioCommand? cmd)
    {
        // Routed through editor.Execute so the drag is undoable like every
        // other mutation. Null cmd (no entities actually moved) is a no-op
        // inside Execute itself, so we don't need to guard here.
        _scenarioData?.Editor.Execute(cmd);
    }

    /// <summary>
    /// Best-effort absolute path for the currently-previewed scenario. Used
    /// only as a fallback start-folder for "Save As" when ScenarioLastSaveDirectory
    /// is empty AND the editor has never been saved before. Returns null for
    /// BAR-archive entries (no on-disk path) and for everything that isn't a
    /// loose file selection.
    /// </summary>
    string? ResolveScenarioSourcePath()
    {
        if (_currentlyPreviewedItem is RootFileEntry rfe && Directory.Exists(_rootDirectory))
            return Path.Combine(_rootDirectory, rfe.RelativePath);
        return null;
    }

    async void OnToolbarSave()
    {
        if (_scenarioData is null) return;
        var ed = _scenarioData.Editor;
        if (ed.SavePath is null) { await OnToolbarSaveAsAsync(); return; }
        await DoSaveTo(_scenarioData, ed.SavePath);
    }

    async void OnToolbarSaveAs() => await OnToolbarSaveAsAsync();

    async Task OnToolbarSaveAsAsync()
    {
        if (_scenarioData is null) return;
        var ed = _scenarioData.Editor;
        var sourcePath = ResolveScenarioSourcePath();

        // Folder priority: explicit per-session memory, then editor.SavePath
        // dir (re-saving an already-saved file), then the source loose-file dir.
        string? startDir = _lastConfiguration?.ScenarioLastSaveDirectory;
        if (string.IsNullOrEmpty(startDir) && ed.SavePath is not null)
            startDir = Path.GetDirectoryName(ed.SavePath);
        if (string.IsNullOrEmpty(startDir) && sourcePath is not null)
            startDir = Path.GetDirectoryName(sourcePath);

        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(startDir))
            startFolder = await StorageProvider.TryGetFolderFromPathAsync(startDir);

        var suggestedName = Path.GetFileName(ed.SavePath ?? sourcePath ?? "untitled.mythscn");
        if (string.IsNullOrEmpty(suggestedName)) suggestedName = "untitled.mythscn";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save scenario as",
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType("AoM Scenario") { Patterns = ["*.mythscn"] },
            ],
            SuggestedStartLocation = startFolder,
        });
        if (file is null) return;

        var path = file.Path.LocalPath;
        await DoSaveTo(_scenarioData, path);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            _lastConfiguration ??= new Configuration();
            _lastConfiguration.ScenarioLastSaveDirectory = dir;
            SaveConfiguration();
        }
    }

    async Task DoSaveTo(ScenarioPreviewData data, string path)
    {
        try
        {
            // Task.Run is legitimate here: every step (FlushParsedViews,
            // ToBytes, CompressL33t, File.Create+Write) is SYNCHRONOUS, and
            // a few-MB scenario can take noticeable time to compress. We're
            // offloading sync work, not wrapping an already-async API.
            await Task.Run(() =>
            {
                data.Scenario.FlushParsedViews(data.Terrain, data.Entities);
                var bytes = data.Scenario.ToBytes();
                var compressed = BarCompression.CompressL33t(bytes);
                using var f = File.Create(path);
                f.Write(compressed.Span);
            });
            data.Editor.MarkSaved(path);
        }
        catch (Exception ex)
        {
            await ShowError("Save failed:\n" + ex.Message);
        }
    }

    async void OnToolbarDiscard()
    {
        if (_scenarioData is null) return;
        var ed = _scenarioData.Editor;
        if (!ed.IsDirty) return;
        var ok = await Confirm("Discard changes?", $"Discard {ed.UndoCount} unsaved change(s)?");
        if (!ok) return;
        ed.Discard();
    }

    /// <summary>
    /// Routed via the scenario root panel's KeyDown so shortcuts only fire
    /// when the 3D editor is focused. Avoids global Ctrl+S/Z stealing keys
    /// from text fields elsewhere in the app.
    /// </summary>
    void ScenarioRoot_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_scenarioData is null) return;
        var ed = _scenarioData.Editor;
        var ctrl  = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift)   != 0;
        if (!ctrl) return;

        switch (e.Key)
        {
            case Key.S when shift:
                _ = OnToolbarSaveAsAsync();
                e.Handled = true;
                break;
            case Key.S:
                OnToolbarSave();
                e.Handled = true;
                break;
            case Key.Z when shift:
                ed.Redo();
                e.Handled = true;
                break;
            case Key.Z:
                ed.Undo();
                e.Handled = true;
                break;
            case Key.Y:
                ed.Redo();
                e.Handled = true;
                break;
        }
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

        // Detach toolbar + inspector from the going-away editor so the dirty
        // dot clears and proto/tile pickers stop dispatching commands at
        // a stale ScenarioEditor instance.
        _scenarioToolbar.Bind(null, sourcePath: null);
        _scenarioInspector.ExecuteCommand = null;
        if (_scenarioGl is not null)
            _scenarioGl.MoveCommitted -= OnDragMoveCommitted;

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
