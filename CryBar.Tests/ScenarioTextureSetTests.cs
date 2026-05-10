using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioTextureSetTests
{
    [Fact]
    public void Build_EnumeratesOnlyTileReferencedPairs()
    {
        var groups = new[]
        {
            new TerrainTextureGroup { Name = "G0", Textures = ["a", "b", "c"] },
            new TerrainTextureGroup { Name = "G1", Textures = ["d", "e"] }
        };
        // 4 tiles touch (0,0), (0,0), (1,1), (0,2). "b" / "d" are declared but
        // unused -- they must NOT inflate the loader's working set.
        var terrain = MakeTerrain(groups,
            tileGroups: [0, 0, 1, 0],
            tileSubs:   [0, 0, 1, 2]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Equal(3, set.Names.Count);
        Assert.Contains("a", set.Names);
        Assert.Contains("c", set.Names);
        Assert.Contains("e", set.Names);
        Assert.DoesNotContain("b", set.Names);
        Assert.DoesNotContain("d", set.Names);

        Assert.True(set.SliceIndices.ContainsKey((0, 0)));
        Assert.True(set.SliceIndices.ContainsKey((0, 2)));
        Assert.True(set.SliceIndices.ContainsKey((1, 1)));
        Assert.False(set.SliceIndices.ContainsKey((0, 1)));
        Assert.False(set.SliceIndices.ContainsKey((1, 0)));
    }

    [Fact]
    public void Build_TileWithUndeclaredPair_NotInSet()
    {
        // Tile referencing a (g, s) that doesn't exist in TerrainGroups should
        // not fabricate a slot.
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
        // Both (0,0) and (0,1) referenced so Build registers both.
        var terrain = MakeTerrain(groups, tileGroups: [0, 0], tileSubs: [0, 1]);
        var set = ScenarioTextureSet.Build(terrain);

        int idx = set.EnsureSlot(0, 1, "b", out var added);

        Assert.Equal(1, idx);
        Assert.Null(added);
        Assert.Equal(2, set.Names.Count);
    }

    [Fact]
    public void EnsureSlot_DeclaredButUnusedPair_AppendsOnDemand()
    {
        // (0,1) is declared but no tile uses it -- Build skips it. Picking it
        // from the inspector calls EnsureSlot which appends a new slot and
        // signals the host to load the DDT lazily.
        var groups = new[] { new TerrainTextureGroup { Name = "G0", Textures = ["a", "b"] } };
        var terrain = MakeTerrain(groups, tileGroups: [0], tileSubs: [0]);
        var set = ScenarioTextureSet.Build(terrain);

        Assert.Single(set.Names);
        Assert.False(set.SliceIndices.ContainsKey((0, 1)));

        int idx = set.EnsureSlot(0, 1, "b", out var added);

        Assert.Equal(1, idx);
        Assert.Equal(1, added);
        Assert.Equal(2, set.Names.Count);
        Assert.Equal("b", set.Names[1]);
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
        // Tile-referenced pairs are pulled (since Build only walks used pairs)
        // so each variant gets evaluated and rejected.
        var groups = new[]
        {
            new TerrainTextureGroup { Name = "G0", Textures = ["Water", "WATER", "wAtEr", "real"] }
        };
        var terrain = MakeTerrain(groups,
            tileGroups: [0, 0, 0, 0],
            tileSubs:   [0, 1, 2, 3]);

        var set = ScenarioTextureSet.Build(terrain);

        Assert.Single(set.Names);
        Assert.Equal("real", set.Names[0]);
        Assert.False(set.SliceIndices.ContainsKey((0, 0)));
        Assert.False(set.SliceIndices.ContainsKey((0, 1)));
        Assert.False(set.SliceIndices.ContainsKey((0, 2)));
        Assert.True(set.SliceIndices.ContainsKey((0, 3)));
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
