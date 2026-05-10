using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioTextureSetTests
{
    [Fact]
    public void Build_EnumeratesAllDeclaredTextures_GroupMajorSubMinor()
    {
        var groups = new[]
        {
            new TerrainTextureGroup { Name = "G0", Textures = ["a", "b", "c"] },
            new TerrainTextureGroup { Name = "G1", Textures = ["d", "e"] }
        };
        // Tile usage covers only a subset; Build must still enumerate every
        // declared (g, s) so subsequently picking an unused texture maps to a
        // valid slice instead of -1.
        var terrain = MakeTerrain(groups,
            tileGroups: [0, 0, 1, 0],
            tileSubs:   [0, 0, 1, 2]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Equal(5, set.Names.Count);
        Assert.Equal("a", set.Names[0]);
        Assert.Equal("b", set.Names[1]);
        Assert.Equal("c", set.Names[2]);
        Assert.Equal("d", set.Names[3]);
        Assert.Equal("e", set.Names[4]);

        Assert.Equal(0, set.SliceIndices[(0, 0)]);
        Assert.Equal(1, set.SliceIndices[(0, 1)]);
        Assert.Equal(2, set.SliceIndices[(0, 2)]);
        Assert.Equal(3, set.SliceIndices[(1, 0)]);
        Assert.Equal(4, set.SliceIndices[(1, 1)]);
    }

    [Fact]
    public void Build_TileWithUndeclaredPair_NotInSet()
    {
        // Tile usage referencing a (g, s) that doesn't exist in TerrainGroups
        // should not fabricate a slot. Build only emits declared pairs.
        var groups = new[] { new TerrainTextureGroup { Name = "G0", Textures = ["a"] } };
        var terrain = MakeTerrain(groups, tileGroups: [0, 5], tileSubs: [0, 99]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Single(set.Names);
        Assert.Equal("a", set.Names[0]);
        Assert.False(set.SliceIndices.ContainsKey((5, 99)));
    }

    [Fact]
    public void EnsureSlot_ExistingPair_ReturnsExistingIndex_NoAppend()
    {
        var groups = new[] { new TerrainTextureGroup { Name = "G0", Textures = ["a", "b"] } };
        var terrain = MakeTerrain(groups, tileGroups: [0], tileSubs: [0]);
        var set = ScenarioTextureSet.Build(terrain);

        int idx = set.EnsureSlot(0, 1, "b", out var added);

        Assert.Equal(1, idx);
        Assert.Null(added);
        Assert.Equal(2, set.Names.Count);
    }

    [Fact]
    public void EnsureSlot_NewPair_AppendsAndReportsNewIndex()
    {
        var groups = new[] { new TerrainTextureGroup { Name = "G0", Textures = ["a"] } };
        var terrain = MakeTerrain(groups, tileGroups: [0], tileSubs: [0]);
        var set = ScenarioTextureSet.Build(terrain);

        int idx = set.EnsureSlot(2, 5, "newtex", out var added);

        Assert.Equal(1, idx);
        Assert.Equal(1, added);
        Assert.Equal(2, set.Names.Count);
        Assert.Equal("newtex", set.Names[1]);
        Assert.Equal(1, set.SliceIndices[(2, 5)]);
    }

    [Fact]
    public void Build_SkipsPseudoWaterTexture()
    {
        // "water" is a pseudo-terrain rendered via the WaterMesh + shader path;
        // there is no water_basecolor.ddt / water.ddt to find. Including it in
        // Names would surface as a permanent "1 missing texture" warning in the
        // inspector. Build must omit it from both Names and SliceIndices.
        var groups = new[]
        {
            new TerrainTextureGroup { Name = "Water", Textures = ["water"] },
            new TerrainTextureGroup { Name = "Ground", Textures = ["grass", "dirt"] }
        };
        var terrain = MakeTerrain(groups,
            tileGroups: [0, 1, 1],
            tileSubs:   [0, 0, 1]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Equal(2, set.Names.Count);
        Assert.DoesNotContain("water", set.Names);
        Assert.Contains("grass", set.Names);
        Assert.Contains("dirt", set.Names);
        // (0, 0) was the water slot -- must NOT be mapped, so TerrainMeshBuilder
        // returns slice = -1 for water tiles (rendered separately via WaterMesh).
        Assert.False(set.SliceIndices.ContainsKey((0, 0)));
        Assert.True(set.SliceIndices.ContainsKey((1, 0)));
        Assert.True(set.SliceIndices.ContainsKey((1, 1)));
    }

    [Fact]
    public void Build_SkipsPseudoWater_CaseInsensitive()
    {
        // Filter is case-insensitive: "Water", "WATER", "wAtEr" all skipped.
        var groups = new[]
        {
            new TerrainTextureGroup { Name = "G0", Textures = ["Water", "WATER", "wAtEr", "real"] }
        };
        var terrain = MakeTerrain(groups, tileGroups: [0], tileSubs: [0]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Single(set.Names);
        Assert.Equal("real", set.Names[0]);
    }

    [Fact]
    public void EnsureSlot_PseudoWater_ReturnsNegativeOne_NoAppend()
    {
        // EnsureSlot also guards against caller-supplied "water" so a tile-texture
        // pick never grows Names with the pseudo-terrain.
        var groups = new[] { new TerrainTextureGroup { Name = "G0", Textures = ["a"] } };
        var terrain = MakeTerrain(groups, tileGroups: [0], tileSubs: [0]);
        var set = ScenarioTextureSet.Build(terrain);

        int idx = set.EnsureSlot(7, 7, "water", out var added);

        Assert.Equal(-1, idx);
        Assert.Null(added);
        Assert.Single(set.Names);
        Assert.False(set.SliceIndices.ContainsKey((7, 7)));
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
