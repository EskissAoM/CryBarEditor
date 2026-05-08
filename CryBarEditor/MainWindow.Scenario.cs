using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CryBar.Scenario;
using CryBarEditor.Classes;
using CryBarEditor.Controls;

namespace CryBarEditor;

public partial class MainWindow
{
    ScenarioPreviewData? _scenarioData;
    GlScenarioPreviewControl? _scenarioGl;
    CancellationTokenSource? _scenarioCts;

    void ShowScenarioPreview(ScenarioFile scenario)
    {
        HideTmmPreview();
        HideScenarioPreview();

        _flatPreview.IsVisible = false;
        _scenarioTabControl.IsVisible = true;

        _scenarioXmlEditor.Document = new TextDocument(ScenarioFile.StripBinaryForPreview(scenario.ToXml()));

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

        _scenarioCts?.Cancel();
        _scenarioCts?.Dispose();
        _scenarioCts = CancellationTokenSource.CreateLinkedTokenSource(data.Cancellation.Token);
        _ = LoadScenarioTexturesAsync(data, _scenarioCts.Token);
    }

    GlScenarioPreviewControl CreateScenarioGl()
    {
        var gl = new GlScenarioPreviewControl();
        gl.CursorHit += hit =>
            Dispatcher.UIThread.Post(() => _scenarioInspector.SetCursor(hit, _scenarioData));
        gl.LoadProgressChanged += p =>
            Dispatcher.UIThread.Post(() => UpdateScenarioProgress(p));
        gl.ErrorChanged += msg =>
            Dispatcher.UIThread.Post(() =>
            {
                _scenarioErrorText.Text = msg ?? "";
                _scenarioErrorPanel.IsVisible = msg is not null;
            });
        return gl;
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

        var resolver = new ScenarioTextureLoader.NameResolver(_fileIndex, ReadFromIndexEntryPooledAsync);

        try
        {
            await ScenarioTextureLoader.LoadAllAsync(
                data,
                resolver,
                (sliceIdx, rgba) => _scenarioGl.UploadSliceAsync(sliceIdx, rgba),
                p => Dispatcher.UIThread.Post(() => UpdateScenarioProgress(p)),
                ct);
        }
        catch (OperationCanceledException) { /* expected on scenario change */ }
        finally
        {
            Dispatcher.UIThread.Post(() => _scenarioProgressOverlay.IsVisible = false);
        }
    }

    void HideScenarioPreview()
    {
        _scenarioCts?.Cancel();
        _scenarioCts?.Dispose();
        _scenarioCts = null;

        _scenarioData?.Dispose();
        _scenarioData = null;

        if (_scenarioTabControl.IsVisible)
        {
            _scenarioTabControl.IsVisible = false;
            _flatPreview.IsVisible = true;
        }
        _scenarioProgressOverlay.IsVisible = false;
        _scenarioErrorPanel.IsVisible = false;

        if (_scenarioGl is not null)
            _scenarioGl.SetScenario(null);
    }
}
