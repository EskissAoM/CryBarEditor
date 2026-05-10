using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CryBar.Scenario;
using CryBar.Scenario.Editor;

namespace CryBarEditor.Classes;

public enum TextureSource : byte
{
    Placeholder = 0,
    Index = 1,
    ManualBar = 2,
}

public sealed class ScenarioPreviewData : IDisposable
{
    public required ScenarioFile Scenario { get; init; }
    public required ScenarioTerrain Terrain { get; init; }
    public required ScenarioTextureSet TextureSet { get; init; }
    // Mutable so the renderer can re-source these from live terrain when a
    // command publishes RenderHint.TerrainGeometry/TerrainTexture/TerrainWater.
    // The cached arrays are stale once heights/tile groups change in place.
    public required TerrainMesh TerrainMesh { get; set; }
    public WaterMesh? WaterMesh { get; set; }
    public required List<ScenarioEntity> Entities { get; init; }

    bool[] _sliceReady = [];
    public IReadOnlyList<bool> SliceReady => _sliceReady;
    internal void MarkSliceReady(int index)
    {
        if ((uint)index < (uint)_sliceReady.Length)
            _sliceReady[index] = true;
    }

    TextureSource[] _textureSources = [];
    public IReadOnlyList<TextureSource> TextureSources => _textureSources;
    internal void SetTextureSource(int sliceIndex, TextureSource source)
    {
        if ((uint)sliceIndex < (uint)_textureSources.Length)
            _textureSources[sliceIndex] = source;
    }

    /// <summary>
    /// Resizes the internal per-slice arrays (SliceReady, TextureSources) to
    /// at least <paramref name="capacity"/> entries. Used after a runtime
    /// EnsureSlot append on TextureSet so the per-slice tracking arrays don't
    /// fall behind Names.Count.
    /// </summary>
    public void EnsureSlotCapacity(int capacity)
    {
        if (capacity <= _sliceReady.Length) return;
        var newReady = new bool[capacity];
        Array.Copy(_sliceReady, newReady, _sliceReady.Length);
        _sliceReady = newReady;

        var newSources = new TextureSource[capacity];
        Array.Copy(_textureSources, newSources, _textureSources.Length);
        _textureSources = newSources;
    }

    public CancellationTokenSource Cancellation { get; } = new();
    public ScenarioSelection Selection { get; } = new();

    // Mediator that owns undo/redo + dirty tracking. Wired in TryBuild after the
    // entity list is materialised so the editor and renderer share the SAME
    // List<ScenarioEntity> instance (mutations from commands are visible everywhere).
    public ScenarioEditor Editor { get; private set; } = null!;

    // Lazy full game-wide proto-name list (from proto.xml.XMB) populated by
    // MainWindow on first proto picker open. Null until then. The picker falls
    // back to ProtoTable when this stays null (no FileIndex / proto.xml not found).
    public List<string>? ProtoNamesCache { get; set; }

    // Lazy full game-wide terrain (group, texture) list (from
    // data/map_definitions/terrain_types.xml.XMB) populated by MainWindow on
    // first terrain picker open. Null until then. The picker falls back to a
    // synthetic cache built from the scenario's own TerrainGroups when this
    // stays null (no FileIndex / terrain_types.xml not found).
    public TerrainTypesCache? TerrainTypesCache { get; set; }

    // Live, editable scenario TM table -- the proto names referenced by entity
    // protoIndex. Populated in TryBuild from the first TM/PT sub-section.
    // SetEntityProtos appends to this when the user picks a name not yet
    // present, and FlushParsedViews regenerates the TM bytes from it on save.
    public List<string> ProtoTable { get; } = new();

    Dictionary<uint, int>? _entityIdToIndex;
    // Lazy id -> Entities[] index lookup. Built on first access; subsequent
    // overlay/inspector rebuilds reuse the same dictionary.
    public IReadOnlyDictionary<uint, int> EntityIdToIndex
    {
        get
        {
            if (_entityIdToIndex is null)
            {
                var d = new Dictionary<uint, int>(Entities.Count);
                for (int i = 0; i < Entities.Count; i++)
                    d[Entities[i].EntityId] = i;
                _entityIdToIndex = d;
            }
            return _entityIdToIndex;
        }
    }

    public static ScenarioPreviewData? TryBuild(ScenarioFile scenario)
    {
        if (scenario is null || !scenario.Parsed) return null;

        var terrain = ScenarioTerrain.TryParse(scenario);
        if (terrain is null) return null;

        int expectedVertices = (terrain.MapSizeX + 1) * (terrain.MapSizeZ + 1);
        int expectedTiles = terrain.MapSizeX * terrain.MapSizeZ;
        if (terrain.MapSizeX <= 0 || terrain.MapSizeX > 1024) return null;
        if (terrain.MapSizeZ <= 0 || terrain.MapSizeZ > 1024) return null;
        if (terrain.Heights.Length != expectedVertices) return null;
        if (terrain.TileGroups.Length != expectedTiles) return null;
        if (terrain.TileSubs.Length != expectedTiles) return null;

        var textureSet = ScenarioTextureSet.Build(terrain);
        var mesh = TerrainMeshBuilder.Build(terrain, textureSet);
        var water = WaterMeshBuilder.Build(terrain);
        var entities = ScenarioEntityListBuilder.Build(scenario);
        var entitiesList = entities.ToList();

        var data = new ScenarioPreviewData
        {
            Scenario = scenario,
            Terrain = terrain,
            TextureSet = textureSet,
            TerrainMesh = mesh,
            WaterMesh = water,
            Entities = entitiesList,
        };
        data._sliceReady = new bool[textureSet.Names.Count];
        data._textureSources = new TextureSource[textureSet.Names.Count];

        // Populate the editable scenario proto table from the first TM/PT sub-section.
        // SetEntityProtos appends to this list; FlushParsedViews writes it back on save.
        var j1 = scenario.GetJ1();
        if (j1 is not null && j1.Parsed)
        {
            foreach (var sub in j1.Sections!)
            {
                if (sub.Marker == "TM" || sub.Marker == "PT")
                {
                    data.ProtoTable.AddRange(ScenarioFile.ReadTmStrings(sub.Data));
                    break;
                }
            }
        }

        // Same terrain + entities list passed to the editor as the renderer reads,
        // so command Apply mutations are immediately visible in both places.
        data.Editor = new ScenarioEditor(scenario, terrain, data.Entities);
        return data;
    }

    public void Dispose()
    {
        Cancellation.Cancel();
        Cancellation.Dispose();
    }
}
