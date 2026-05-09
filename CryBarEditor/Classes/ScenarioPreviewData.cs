using System;
using System.Collections.Generic;
using System.Threading;
using CryBar.Scenario;

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
    public required TerrainMesh TerrainMesh { get; init; }
    public WaterMesh? WaterMesh { get; init; }
    public required EntityMarker[] Entities { get; init; }

    bool[] _sliceReady = [];
    public IReadOnlyList<bool> SliceReady => _sliceReady;
    internal void MarkSliceReady(int index) => _sliceReady[index] = true;

    TextureSource[] _textureSources = [];
    public IReadOnlyList<TextureSource> TextureSources => _textureSources;
    internal void SetTextureSource(int sliceIndex, TextureSource source)
    {
        if ((uint)sliceIndex < (uint)_textureSources.Length)
            _textureSources[sliceIndex] = source;
    }

    public CancellationTokenSource Cancellation { get; } = new();
    public ScenarioSelection Selection { get; } = new();

    Dictionary<uint, int>? _entityIdToIndex;
    // Lazy id -> Entities[] index lookup. Built on first access; subsequent
    // overlay/inspector rebuilds reuse the same dictionary.
    public IReadOnlyDictionary<uint, int> EntityIdToIndex
    {
        get
        {
            if (_entityIdToIndex is null)
            {
                var d = new Dictionary<uint, int>(Entities.Length);
                for (int i = 0; i < Entities.Length; i++)
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
        var entities = EntityOverlayBuilder.Build(scenario);

        var data = new ScenarioPreviewData
        {
            Scenario = scenario,
            Terrain = terrain,
            TextureSet = textureSet,
            TerrainMesh = mesh,
            WaterMesh = water,
            Entities = entities,
        };
        data._sliceReady = new bool[textureSet.Names.Count];
        data._textureSources = new TextureSource[textureSet.Names.Count];
        return data;
    }

    public void Dispose()
    {
        Cancellation.Cancel();
        Cancellation.Dispose();
    }
}
