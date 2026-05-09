using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioTextureSetTests
{
    [Fact]
    public void Build_DedupesRepeatedPairs()
    {
        var groups = new[]
        {
            new TerrainTextureGroup { Name = "G0", Textures = ["a", "b", "c"] },
            new TerrainTextureGroup { Name = "G1", Textures = ["d", "e"] }
        };
        var terrain = MakeTerrain(groups,
            tileGroups: [0, 0, 1, 0],
            tileSubs:   [0, 0, 1, 2]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Equal(3, set.Names.Count);
        Assert.Equal("a", set.Names[0]);
        Assert.Equal("e", set.Names[1]);
        Assert.Equal("c", set.Names[2]);
        Assert.Equal(0, set.SliceIndices[(0, 0)]);
        Assert.Equal(1, set.SliceIndices[(1, 1)]);
        Assert.Equal(2, set.SliceIndices[(0, 2)]);
    }

    [Fact]
    public void Build_MissingPair_OmittedFromSet()
    {
        var groups = new[] { new TerrainTextureGroup { Name = "G0", Textures = ["a"] } };
        var terrain = MakeTerrain(groups, tileGroups: [0, 5], tileSubs: [0, 99]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Single(set.Names);
        Assert.Equal("a", set.Names[0]);
        Assert.False(set.SliceIndices.ContainsKey((5, 99)));
    }

    static ScenarioTerrain MakeTerrain(TerrainTextureGroup[] groups, byte[] tileGroups, ushort[] tileSubs)
    {
        return new ScenarioTerrain
        {
            MapSizeX = 2, MapSizeZ = 2,
            Heights = new float[9], WaterHeights = new float[9], UnkHeights = new float[9],
            TileGroups = tileGroups,
            TileSubs = tileSubs,
            TilePt = new byte[tileGroups.Length],
            WaterType = new byte[tileGroups.Length],
            TerrainGroups = groups
        };
    }
}
