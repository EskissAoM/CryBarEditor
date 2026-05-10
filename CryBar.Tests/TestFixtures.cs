using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

/// <summary>
/// Shared cheap test fixtures for tests that need a ScenarioFile / ScenarioTerrain
/// reference but don't care about its byte content.
/// </summary>
public static class TestFixtures
{
    /// <summary>
    /// Loads the canonical fixture mythscn (already used by Z1Writer / Tn / FlushAndSave tests).
    /// Cheapest path because ScenarioFile has no parameterless public constructor.
    /// </summary>
    public static ScenarioFile MakeMinimalScenarioFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        return new ScenarioFile(decompressed!);
    }

    /// <summary>
    /// Synthetic 2x2 terrain with a single "grass" group, no water tiles.
    /// Fills every required-init property; sets every optional round-trip field
    /// to its safe default (HasT3=1, HasTm=0, empty opaque sub-sections).
    /// Sufficient for editor tests that hold a reference but don't write the
    /// terrain back through TnWriter.
    /// </summary>
    public static ScenarioTerrain MakeMinimalTerrain()
    {
        // 2x2 tiles -> 3x3 vertex grid (mapSize+1 in each axis).
        const int vertCount = 3 * 3;
        const int tileCount = 2 * 2;

        return new ScenarioTerrain
        {
            MapSizeX = 2,
            MapSizeZ = 2,
            Heights = new float[vertCount],
            WaterHeights = new float[vertCount],
            UnkHeights = new float[vertCount],
            TileGroups = new byte[tileCount],
            TileSubs = new ushort[tileCount],
            TilePt = new byte[tileCount],
            // 255 = no-water sentinel
            WaterType = Enumerable.Repeat((byte)255, tileCount).ToArray(),
            TerrainGroups = new[]
            {
                new TerrainTextureGroup { Name = "grass", Textures = new[] { "grass\\grass" } },
            },
        };
    }
}
