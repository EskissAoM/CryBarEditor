using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using CryBarEditor.Classes;
using CryBarEditor.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CryBarEditor.Controls;

public partial class ScenarioInspectorPanel : UserControl
{
    public ScenarioInspectorPanel()
    {
        InitializeComponent();
    }

    public event System.Action? SelectBarRequested;

    // MainWindow wires this in Task 24 to dispatch through ScenarioEditor.Execute.
    // Null until then; handlers no-op when null.
    public System.Action<IScenarioCommand?>? ExecuteCommand;

    ScenarioPreviewData? _data;

    // Suppress flags guard against ValueChanged/SelectionChanged firing while we
    // populate controls during UpdateSelection (otherwise Populate->Handler->Command
    // would feed ghost commands into the editor).
    bool _suppressWaterChange;
    bool _suppressHeightChange;
    bool _suppressEntityChange;

    // Cached player options used by the entity ComboBox. Built once on first use.
    static List<PlayerOption>? _playerOptions;

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
        _data = data;

        if (data is null || data.Selection.Kind == ScenarioSelectionKind.None)
        {
            _selectedSection.IsVisible = false;
            _tileEditPanel.IsVisible = false;
            _entityEditPanel.IsVisible = false;
            _selectedFields.IsVisible = false;
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
        _tileEditPanel.IsVisible = true;
        _entityEditPanel.IsVisible = false;
        _selectedFields.IsVisible = false;

        var sel = data.Selection;
        int count = sel.Tiles.Count;
        _selectedHeader.Text = count == 1 ? "Selected tile" : $"Selected {count} tiles";

        var terrain = data.Terrain;
        int rowStride = terrain.MapSizeX + 1;

        // Aggregate over selected tiles. Field is MIXED if any value differs.
        float? avgHeight = null; bool heightMixed = false;
        (byte g, ushort s)? sharedTex = null; bool textureMixed = false;
        string? sharedTexName = null;
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
            string tName = "?";
            if (g < terrain.TerrainGroups.Length && s < terrain.TerrainGroups[g].Textures.Length)
                tName = terrain.TerrainGroups[g].Textures[s];

            byte wt = terrain.WaterType[idx];

            if (avgHeight is null) avgHeight = avgH;
            else if (!heightMixed && System.Math.Abs(avgH - avgHeight.Value) > 1e-3f) heightMixed = true;

            if (sharedTex is null) { sharedTex = (g, s); sharedTexName = tName; }
            else if (!textureMixed && (sharedTex.Value.g != g || sharedTex.Value.s != s)) textureMixed = true;

            if (waterType is null) waterType = wt;
            else if (!waterMixed && waterType != wt) waterMixed = true;

            if (count > 1) lines.AppendLine($"({tx}, {tz}) {tName}");
        }

        _tileTextureBtn.Content = textureMixed ? "MIXED" : (sharedTexName ?? "?");

        // Water types are byte 0..255 (255 = no water). Show plain ints in the combo.
        if (_tileWaterCombo.ItemsSource is null)
            _tileWaterCombo.ItemsSource = Enumerable.Range(0, 256).ToArray();
        _suppressWaterChange = true;
        _tileWaterCombo.SelectedIndex = waterMixed ? -1 : (waterType ?? -1);
        _suppressWaterChange = false;

        _suppressHeightChange = true;
        _tileHeightNum.Value = heightMixed ? null : (avgHeight is null ? null : (decimal?)avgHeight.Value);
        _suppressHeightChange = false;

        _selectedListButton.IsVisible = count > 1;
        if (count > 1) _selectedListText.Text = lines.ToString().TrimEnd();
    }

    void UpdateEntitySelection(ScenarioPreviewData data)
    {
        _tileEditPanel.IsVisible = false;
        _entityEditPanel.IsVisible = true;
        _selectedFields.IsVisible = false;

        var sel = data.Selection;
        int count = sel.Entities.Count;
        _selectedHeader.Text = count == 1 ? "Selected entity" : $"Selected {count} entities";

        var idToIdx = data.EntityIdToIndex;

        string? proto = null; bool protoMixed = false;
        byte? player = null; bool playerMixed = false;
        Vector3? position = null; bool positionMixed = false;
        float? yaw = null; bool yawMixed = false;

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
            else if (!positionMixed && Vector3.DistanceSquared(position.Value, m.Position) > 1e-4f)
                positionMixed = true;

            float ey = m.Rotation.ExtractYawDegrees();
            if (yaw is null) yaw = ey;
            else if (!yawMixed && System.Math.Abs(((ey - yaw.Value + 540f) % 360f) - 180f) > 0.1f)
                yawMixed = true;

            if (count > 1) lines.AppendLine($"[{id}] {m.ProtoName}");
        }

        // Populate proto button caption.
        _entityProtoBtn.Content = protoMixed ? "MIXED" : (proto ?? "(none)");

        // Player ComboBox: build options once, then select.
        if (_entityPlayerCombo.ItemsSource is null)
        {
            _playerOptions ??= BuildPlayerOptions();
            _entityPlayerCombo.ItemsSource = _playerOptions;
        }
        _suppressEntityChange = true;
        if (playerMixed || player is null)
            _entityPlayerCombo.SelectedIndex = -1;
        else
            _entityPlayerCombo.SelectedIndex = player.Value < _playerOptions!.Count ? player.Value : -1;
        _suppressEntityChange = false;

        // Position fields. MIXED -> blank.
        _suppressEntityChange = true;
        if (positionMixed || position is null)
        {
            _entityPosX.Value = null;
            _entityPosY.Value = null;
            _entityPosZ.Value = null;
        }
        else
        {
            _entityPosX.Value = (decimal)position.Value.X;
            _entityPosY.Value = (decimal)position.Value.Y;
            _entityPosZ.Value = (decimal)position.Value.Z;
        }

        if (yawMixed || yaw is null)
            _entityYaw.Value = null;
        else
            _entityYaw.Value = (decimal)yaw.Value;
        _suppressEntityChange = false;

        _selectedListButton.IsVisible = count > 1;
        if (count > 1) _selectedListText.Text = lines.ToString().TrimEnd();
    }

    static List<PlayerOption> BuildPlayerOptions()
    {
        var list = new List<PlayerOption>(PlayerColors.Count);
        for (byte i = 0; i < PlayerColors.Count; i++)
        {
            var c = PlayerColors.GetRgb(i);
            var brush = new SolidColorBrush(Color.FromRgb(
                (byte)System.Math.Clamp(c.R * 255f, 0f, 255f),
                (byte)System.Math.Clamp(c.G * 255f, 0f, 255f),
                (byte)System.Math.Clamp(c.B * 255f, 0f, 255f)));
            list.Add(new PlayerOption
            {
                PlayerId = i,
                Label = $"Player {i}",
                Brush = brush,
            });
        }
        return list;
    }

    // ----- Tile edit handlers -----

    async void OnTileTextureClick(object? sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Tiles || sel.Tiles.Count == 0) return;

        var terrain = _data.Terrain;

        // Build flat picker list with group + display split, plus parallel (g, s) ref array.
        var items = new List<PickerItem>();
        var refs = new List<(byte g, ushort s)>();
        for (byte g = 0; g < terrain.TerrainGroups.Length; g++)
        {
            var grp = terrain.TerrainGroups[g];
            for (ushort s = 0; s < grp.Textures.Length; s++)
            {
                items.Add(new PickerItem { Group = grp.Name, Display = grp.Textures[s] });
                refs.Add((g, s));
            }
        }
        if (items.Count == 0) return;

        // Preselect the first selected tile's current texture.
        int firstTileIdx = sel.Tiles.First();
        byte curG = terrain.TileGroups[firstTileIdx];
        ushort curS = terrain.TileSubs[firstTileIdx];
        int preselect = refs.FindIndex(r => r.g == curG && r.s == curS);

        var owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        if (owner is null) return;

        var picker = new PickerWindow("Pick tile texture", items, preselect >= 0 ? preselect : null);
        await picker.ShowDialog(owner);

        if (picker.PickedIndex is not int idx) return;
        var (newG, newS) = refs[idx];

        var tileList = sel.Tiles.ToArray();
        var cmd = SetTileTextures.Create(terrain, tileList, newG, newS);
        ExecuteCommand?.Invoke(cmd);
    }

    void OnTileWaterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWaterChange) return;
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Tiles || sel.Tiles.Count == 0) return;

        int newIdx = _tileWaterCombo.SelectedIndex;
        if (newIdx < 0 || newIdx > 255) return;

        var tileList = sel.Tiles.ToArray();
        var cmd = SetTileWaterTypes.Create(_data.Terrain, tileList, (byte)newIdx);
        ExecuteCommand?.Invoke(cmd);
    }

    void OnTileHeightChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressHeightChange) return;
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Tiles || sel.Tiles.Count == 0) return;

        if (_tileHeightNum.Value is not decimal newDec) return;
        float newH = (float)newDec;

        ApplyHeightAbsolute(_data, sel, newH);
    }

    void OnTileHeightIncrement(object? sender, RoutedEventArgs e) => ApplyHeightDelta(+1f);
    void OnTileHeightDecrement(object? sender, RoutedEventArgs e) => ApplyHeightDelta(-1f);

    void ApplyHeightDelta(float delta)
    {
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Tiles || sel.Tiles.Count == 0) return;

        var terrain = _data.Terrain;
        var verts = VertexHeightHelpers.UniqueCornerVertices(sel.Tiles, terrain.MapSizeX);
        var vertList = verts.ToArray();
        var newH = new float[vertList.Length];
        for (int i = 0; i < vertList.Length; i++)
            newH[i] = terrain.Heights[vertList[i]] + delta;

        var cmd = SetVertexHeights.Create(terrain, vertList, newH);
        ExecuteCommand?.Invoke(cmd);
    }

    void ApplyHeightAbsolute(ScenarioPreviewData data, ScenarioSelection sel, float newHeight)
    {
        var terrain = data.Terrain;
        var verts = VertexHeightHelpers.UniqueCornerVertices(sel.Tiles, terrain.MapSizeX);
        var vertList = verts.ToArray();
        var newH = new float[vertList.Length];
        for (int i = 0; i < vertList.Length; i++) newH[i] = newHeight;

        var cmd = SetVertexHeights.Create(terrain, vertList, newH);
        ExecuteCommand?.Invoke(cmd);
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

    // ----- Entity edit handlers -----

    async void OnEntityProtoClick(object? sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Entities || sel.Entities.Count == 0) return;

        // Proto cache is populated lazily by MainWindow on first open (Task 24).
        // No-op until then; the user just sees nothing happen.
        var protoNames = _data.ProtoNamesCache;
        if (protoNames is null || protoNames.Count == 0) return;

        // Preselect the first selected entity's current proto.
        var idToIdx = _data.EntityIdToIndex;
        int preselect = -1;
        foreach (uint id in sel.Entities)
        {
            if (idToIdx.TryGetValue(id, out int idx))
            {
                int p = _data.Entities[idx].ProtoIndex;
                if (p >= 0 && p < protoNames.Count) preselect = p;
                break;
            }
        }

        var owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        if (owner is null) return;

        var picker = new PickerWindow("Pick proto", protoNames, preselect >= 0 ? preselect : null);
        await picker.ShowDialog(owner);

        if (picker.PickedItem is null) return;
        int newIdx = protoNames.IndexOf(picker.PickedItem);
        if (newIdx < 0) return;

        var ids = sel.Entities.ToArray();
        var cmd = SetEntityProtos.Create(_data.Entities, ids, newIdx, protoNames[newIdx]);
        ExecuteCommand?.Invoke(cmd);
    }

    void OnEntityPlayerChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEntityChange) return;
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Entities || sel.Entities.Count == 0) return;

        int sIdx = _entityPlayerCombo.SelectedIndex;
        if (sIdx < 0 || _playerOptions is null || sIdx >= _playerOptions.Count) return;

        byte newPlayer = _playerOptions[sIdx].PlayerId;
        var ids = sel.Entities.ToArray();
        var cmd = SetEntityPlayers.Create(_data.Entities, ids, newPlayer);
        ExecuteCommand?.Invoke(cmd);
    }

    void OnEntityPosChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressEntityChange) return;
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Entities || sel.Entities.Count == 0) return;

        if (_entityPosX.Value is not decimal dx) return;
        if (_entityPosY.Value is not decimal dy) return;
        if (_entityPosZ.Value is not decimal dz) return;

        var pos = new Vector3((float)dx, (float)dy, (float)dz);
        var ids = sel.Entities.ToArray();
        var newPos = new Vector3[ids.Length];
        for (int i = 0; i < ids.Length; i++) newPos[i] = pos;

        var cmd = SetEntityPositions.Create(_data.Entities, ids, newPos);
        ExecuteCommand?.Invoke(cmd);
    }

    void OnEntityYawChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressEntityChange) return;
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Entities || sel.Entities.Count == 0) return;

        if (_entityYaw.Value is not decimal dyaw) return;

        var rot = Matrix3x3.FromYawDegrees((float)dyaw);
        var ids = sel.Entities.ToArray();
        var newRot = new Matrix3x3[ids.Length];
        for (int i = 0; i < ids.Length; i++) newRot[i] = rot;

        var cmd = SetEntityRotations.Create(_data.Entities, ids, newRot);
        ExecuteCommand?.Invoke(cmd);
    }

    void OnEntityDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        var sel = _data.Selection;
        if (sel.Kind != ScenarioSelectionKind.Entities || sel.Entities.Count == 0) return;

        var ids = sel.Entities.ToArray();
        var cmd = DeleteEntities.Create(_data.Entities, ids);
        ExecuteCommand?.Invoke(cmd);
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

// Bound DataTemplate item for the entity Player ComboBox; uses PlayerColors
// for the swatch fill so renderer + inspector pick the same color.
public sealed class PlayerOption
{
    public required byte PlayerId { get; init; }
    public required string Label { get; init; }
    public required IBrush Brush { get; init; }
}

