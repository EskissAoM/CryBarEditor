using System;
using System.Threading;
using CryBar.Scenario;

namespace CryBarEditor.Classes;

public sealed class ScenarioPreviewData : IDisposable
{
    public required ScenarioFile Scenario { get; init; }
    public required ScenarioTerrain Terrain { get; init; }
    public required ScenarioTextureSet TextureSet { get; init; }
    public required TerrainMesh TerrainMesh { get; init; }
    public WaterMesh? WaterMesh { get; init; }
    public required EntityMarker[] Entities { get; init; }

    // SliceReady[i] flips true once slice i has been uploaded; false entries render with the grass-green placeholder.
    public bool[] SliceReady = [];

    public CancellationTokenSource Cancellation { get; } = new();

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

        return new ScenarioPreviewData
        {
            Scenario = scenario,
            Terrain = terrain,
            TextureSet = textureSet,
            TerrainMesh = mesh,
            WaterMesh = water,
            Entities = entities,
            SliceReady = new bool[textureSet.Names.Count]
        };
    }

    public void Dispose()
    {
        Cancellation.Cancel();
        Cancellation.Dispose();
    }
}
