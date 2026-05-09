using Avalonia.Controls;
using Avalonia.Interactivity;
using CryBar.Scenario;
using CryBarEditor.Classes;

namespace CryBarEditor.Controls;

public partial class ScenarioInspectorPanel : UserControl
{
    public ScenarioInspectorPanel()
    {
        InitializeComponent();
    }

    public event System.Action? SelectBarRequested;

    void SelectBarClick(object? sender, RoutedEventArgs e) => SelectBarRequested?.Invoke();

    public void SetCursor(GlScenarioPreviewControl.WorldRayHit? hit, ScenarioPreviewData? data)
    {
        if (hit is null || data is null)
        {
            _cursorInfo.Text = "(hover map)";
            return;
        }
        var h = hit.Value;
        var groupName = "?"; var texName = "?";
        int tIdx = h.TileZ * data.Terrain.MapSizeX + h.TileX;
        if (tIdx >= 0 && tIdx < data.Terrain.TileGroups.Length)
        {
            var g = data.Terrain.TileGroups[tIdx];
            var s = data.Terrain.TileSubs[tIdx];
            if (g < data.Terrain.TerrainGroups.Length)
            {
                var grp = data.Terrain.TerrainGroups[g];
                groupName = grp.Name;
                if (s < grp.Textures.Length) texName = grp.Textures[s];
            }
        }

        _cursorInfo.Text =
            $"Tile: ({h.TileX}, {h.TileZ})\n" +
            $"Vertex: ({h.VertexX}, {h.VertexZ})\n" +
            $"Height: {h.Height:F2}\n" +
            $"Group: {groupName}\n" +
            $"Texture: {texName}";
    }

    public void SetEntity(EntityMarker? marker)
    {
        if (marker is null)
        {
            _entityInfo.Text = "(click an entity)";
            return;
        }
        _entityInfo.Text =
            $"Proto: {marker.ProtoName}\n" +
            $"Player: {marker.PlayerId}\n" +
            $"Position: ({marker.Position.X:F2}, {marker.Position.Y:F2}, {marker.Position.Z:F2})\n" +
            $"Entity ID: {marker.EntityId}";
    }

    public void UpdateAfterLoad(ScenarioPreviewData? data, string? manualBarPath)
    {
        if (data is null)
        {
            _textureSummary.Text = "(no scenario)";
            _warningsRow.IsVisible = false;
            _selectBarButton.IsVisible = false;
            _manualBarPathText.IsVisible = false;
            return;
        }

        int total = data.TextureSources.Count;
        var counts = new int[3];
        for (int i = 0; i < total; i++)
            counts[(int)data.TextureSources[i]]++;
        int fromBar = counts[(int)TextureSource.ManualBar];
        int missing = counts[(int)TextureSource.Placeholder];

        _textureSummary.Text = (fromBar, missing) switch
        {
            (0, 0)         => $"{total} textures",
            (var b, 0)     => $"{total} textures ({b} from BAR)",
            (0, var m)     => $"{total} textures ({m} missing)",
            (var b, var m) => $"{total} textures ({b} from BAR, {m} missing)",
        };

        if (missing > 0)
        {
            var names = data.TextureSet.Names;
            var missingList = new System.Collections.Generic.List<string>(missing);
            for (int i = 0; i < total; i++)
                if (data.TextureSources[i] == TextureSource.Placeholder)
                    missingList.Add(names[i]);
            _missingNamesText.Text = string.Join('\n', missingList);

            _warningsText.Text = $"! {missing} of {total} textures missing";
            _warningsRow.IsVisible = true;
            _selectBarButton.IsVisible = true;
        }
        else
        {
            _warningsRow.IsVisible = false;
            _selectBarButton.IsVisible = false;
        }

        // Null path leaves the caption alone so a prior "Failed to load"
        // status survives this post-load refresh until the user picks again.
        if (manualBarPath is not null)
            SetManualBarStatus(manualBarPath, loadFailed: false);
    }

    public void SetManualBarStatus(string? path, bool loadFailed)
    {
        if (path is null)
        {
            _manualBarPathText.IsVisible = false;
            return;
        }
        _manualBarPathText.Text = loadFailed
            ? $"Failed to load: {System.IO.Path.GetFileName(path)}"
            : $"Fallback: {System.IO.Path.GetFileName(path)}";
        _manualBarPathText.Foreground = loadFailed
            ? Avalonia.Media.Brushes.Salmon
            : Avalonia.Media.Brushes.Gray;
        _manualBarPathText.IsVisible = true;
    }
}
