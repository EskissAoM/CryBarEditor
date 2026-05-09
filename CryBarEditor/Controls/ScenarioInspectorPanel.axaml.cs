using Avalonia.Controls;
using Avalonia.Interactivity;
using CryBar.Scenario;
using CryBarEditor.Classes;
using System.Collections.Generic;

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

    public event System.Action? ClearSelectionRequested;

    void ClearSelectionClick(object? sender, RoutedEventArgs e) => ClearSelectionRequested?.Invoke();

    public void UpdateSelection(ScenarioPreviewData? data)
    {
        if (data is null || data.Selection.Kind == ScenarioSelectionKind.None)
        {
            _selectedSection.IsVisible = false;
            return;
        }

        _selectedSection.IsVisible = true;

        if (data.Selection.Kind == ScenarioSelectionKind.Tiles)
            UpdateTileSelection(data);
        else
            UpdateEntitySelection(data);
    }

    void UpdateTileSelection(ScenarioPreviewData data)
    {
        var sel = data.Selection;
        int count = sel.Tiles.Count;
        _selectedHeader.Text = count == 1 ? "Selected tile" : $"Selected {count} tiles";

        var terrain = data.Terrain;
        int rowStride = terrain.MapSizeX + 1;

        // Aggregate over selected tiles. Field is MIXED if any value differs.
        float? height = null; bool heightMixed = false;
        string? group = null; bool groupMixed = false;
        string? texture = null; bool textureMixed = false;
        byte? waterType = null; bool waterMixed = false;

        var lines = new System.Text.StringBuilder();
        foreach (int idx in sel.Tiles)
        {
            int tx = idx % terrain.MapSizeX;
            int tz = idx / terrain.MapSizeX;

            float h00 = terrain.Heights[tz       * rowStride + tx    ];
            float h10 = terrain.Heights[tz       * rowStride + tx + 1];
            float h11 = terrain.Heights[(tz + 1) * rowStride + tx + 1];
            float h01 = terrain.Heights[(tz + 1) * rowStride + tx    ];
            float avgH = (h00 + h10 + h11 + h01) * 0.25f;

            byte g = terrain.TileGroups[idx];
            ushort s = terrain.TileSubs[idx];
            string gName = "?", tName = "?";
            if (g < terrain.TerrainGroups.Length)
            {
                gName = terrain.TerrainGroups[g].Name;
                if (s < terrain.TerrainGroups[g].Textures.Length)
                    tName = terrain.TerrainGroups[g].Textures[s];
            }

            byte wt = terrain.WaterType[idx];

            if (height is null) height = avgH;
            else if (!heightMixed && System.Math.Abs(avgH - height.Value) > 1e-3f) heightMixed = true;

            if (group is null) group = gName;
            else if (!groupMixed && group != gName) groupMixed = true;

            if (texture is null) texture = tName;
            else if (!textureMixed && texture != tName) textureMixed = true;

            if (waterType is null) waterType = wt;
            else if (!waterMixed && waterType != wt) waterMixed = true;

            if (count > 1) lines.AppendLine($"({tx}, {tz}) {tName}");
        }

        _selectedFields.Text =
            $"Height: {(heightMixed ? "MIXED" : (height?.ToString("F2") ?? "?"))}\n" +
            $"Group: {(groupMixed ? "MIXED" : group ?? "?")}\n" +
            $"Texture: {(textureMixed ? "MIXED" : texture ?? "?")}\n" +
            $"Water type: {(waterMixed ? "MIXED" : waterType?.ToString() ?? "?")}";

        _selectedListButton.IsVisible = count > 1;
        if (count > 1) _selectedListText.Text = lines.ToString().TrimEnd();
    }

    void UpdateEntitySelection(ScenarioPreviewData data)
    {
        var sel = data.Selection;
        int count = sel.Entities.Count;
        _selectedHeader.Text = count == 1 ? "Selected entity" : $"Selected {count} entities";

        // Build id->index lookup once.
        var idToIdx = new Dictionary<uint, int>(data.Entities.Length);
        for (int i = 0; i < data.Entities.Length; i++)
            idToIdx[data.Entities[i].EntityId] = i;

        string? proto = null; bool protoMixed = false;
        int? player = null; bool playerMixed = false;
        bool positionMixed = false; System.Numerics.Vector3? position = null;

        var lines = new System.Text.StringBuilder();
        foreach (uint id in sel.Entities)
        {
            if (!idToIdx.TryGetValue(id, out int idx)) continue;
            var m = data.Entities[idx];

            if (proto is null) proto = m.ProtoName;
            else if (!protoMixed && proto != m.ProtoName) protoMixed = true;

            if (player is null) player = m.PlayerId;
            else if (!playerMixed && player != m.PlayerId) playerMixed = true;

            if (position is null) position = m.Position;
            else if (!positionMixed && System.Numerics.Vector3.DistanceSquared(position.Value, m.Position) > 1e-4f)
                positionMixed = true;

            if (count > 1) lines.AppendLine($"[{id}] {m.ProtoName}");
        }

        string posText = position is null
            ? "?"
            : positionMixed
                ? "MIXED"
                : $"({position.Value.X:F2}, {position.Value.Y:F2}, {position.Value.Z:F2})";

        _selectedFields.Text =
            $"Proto: {(protoMixed ? "MIXED" : proto ?? "?")}\n" +
            $"Player: {(playerMixed ? "MIXED" : player?.ToString() ?? "?")}\n" +
            $"Position: {posText}";

        _selectedListButton.IsVisible = count > 1;
        if (count > 1) _selectedListText.Text = lines.ToString().TrimEnd();
    }

    public void UpdateAfterLoad(ScenarioPreviewData? data)
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
