using CryBar.Scenario;

namespace CryBar.Tests;

public class WaterMeshBuilderTests
{
    [Fact]
    public void Build_NoWaterHeights_ReturnsNull()
    {
        var terrain = NewTerrain(2, 2, waterHeights: new float[0]);
        Assert.Null(WaterMeshBuilder.Build(terrain));
    }

    [Fact]
    public void Build_AllZeroWaterHeights_ReturnsNull()
    {
        // 2x2 map -> 3x3 vertex grid = 9 entries, all zero -> no water tiles
        var terrain = NewTerrain(2, 2, waterHeights: new float[9]);
        Assert.Null(WaterMeshBuilder.Build(terrain));
    }

    [Fact]
    public void Build_OneTileHasWater_EmitsOneQuad()
    {
        // 2x2 map. Vertex grid is 3x3 indexed (vz * 3 + vx). Set water level
        // and mark only tile (0,0) as a water-textured tile.
        var heights = new float[9];
        heights[0] = 5; heights[1] = 5; heights[3] = 5; heights[4] = 5;

        var water = WaterMeshBuilder.Build(NewWaterTerrain(2, 2, heights, waterTiles: [0]));

        Assert.NotNull(water);
        Assert.Equal(4, water!.VertexCount);
        Assert.Equal(6, water.IndexCount);
        Assert.All(water.Indices, i => Assert.True(i < water.VertexCount));
        // Quad spans tile (0,0): X in {0,1}, Z in {0,1}.
        for (int i = 0; i < water.VertexCount; i++)
        {
            float x = water.Vertices[i * 3 + 0];
            float z = water.Vertices[i * 3 + 2];
            Assert.True(x == 0 || x == 1);
            Assert.True(z == 0 || z == 1);
        }
    }

    [Fact]
    public void Build_HoleWithoutWaterTexture_EmitsNothing()
    {
        // WaterHeights set everywhere (sea level), but no water-textured tile.
        // A hole in the heightmap must NOT spawn a water surface on its own.
        var heights = new float[9];
        for (int i = 0; i < heights.Length; i++) heights[i] = 5;

        var water = WaterMeshBuilder.Build(NewWaterTerrain(2, 2, heights, waterTiles: []));

        Assert.Null(water);
    }

    [Fact]
    public void Build_TwoDisjointBodies_GetIndependentLevels()
    {
        // 4x1 strip with water at tiles 0 and 3 (separated by dry tiles 1,2).
        // Vertex grid is 5x2 = 10 entries. Body A sits at h=2, body B at h=20.
        // A global-median renderer would snap both bodies to ~11; per-component
        // rendering gives each body its own y.
        var heights = new float[10];
        // tile 0 corners: v indices 0,1,5,6
        heights[0] = 2; heights[1] = 2; heights[5] = 2; heights[6] = 2;
        // tile 3 corners: v indices 3,4,8,9
        heights[3] = 20; heights[4] = 20; heights[8] = 20; heights[9] = 20;

        var water = WaterMeshBuilder.Build(NewWaterTerrain(4, 1, heights, waterTiles: [0, 3]))!;

        // Two disjoint quads, no shared vertices (vertex map is per-component)
        Assert.Equal(8, water.VertexCount);
        Assert.Equal(12, water.IndexCount);

        // Two distinct y levels show up, each at body-height minus ZBias.
        var ys = new HashSet<float>();
        for (int i = 0; i < water.VertexCount; i++) ys.Add(water.Vertices[i * 3 + 1]);
        Assert.Equal(2, ys.Count);
        Assert.Contains(2f - WaterMeshBuilder.ZBias, ys);
        Assert.Contains(20f - WaterMeshBuilder.ZBias, ys);
    }

    [Fact]
    public void Build_FlatHeightAtMedianMinusBias()
    {
        var heights = new float[9];
        heights[0] = 4; heights[1] = 5; heights[3] = 5; heights[4] = 6;

        var water = WaterMeshBuilder.Build(NewWaterTerrain(2, 2, heights, waterTiles: [0]))!;

        // Emitted Y = median(5) - ZBias
        float expectedY = 5f - WaterMeshBuilder.ZBias;
        for (int i = 0; i < water.VertexCount; i++)
            Assert.Equal(expectedY, water.Vertices[i * 3 + 1], 4);
    }

    static ScenarioTerrain NewTerrain(int mapX, int mapZ, float[] waterHeights)
    {
        int vCount = (mapX + 1) * (mapZ + 1);
        int tCount = mapX * mapZ;
        return new ScenarioTerrain
        {
            MapSizeX = mapX, MapSizeZ = mapZ,
            Heights = new float[vCount],
            WaterHeights = waterHeights,
            UnkHeights = new float[vCount],
            TileGroups = new byte[tCount], TileSubs = new ushort[tCount], TilePt = new byte[tCount],
            WaterType = new byte[tCount],
            TerrainGroups = []
        };
    }

    // Variant that wires WaterType so the listed tiles are water (value 0) and
    // all others are non-water (value 1). Matches WaterMeshBuilder's convention.
    static ScenarioTerrain NewWaterTerrain(int mapX, int mapZ, float[] waterHeights, int[] waterTiles)
    {
        int vCount = (mapX + 1) * (mapZ + 1);
        int tCount = mapX * mapZ;
        var waterType = new byte[tCount];
        Array.Fill(waterType, (byte)1); // non-water default
        foreach (var t in waterTiles) waterType[t] = 0;
        return new ScenarioTerrain
        {
            MapSizeX = mapX, MapSizeZ = mapZ,
            Heights = new float[vCount],
            WaterHeights = waterHeights,
            UnkHeights = new float[vCount],
            TileGroups = new byte[tCount], TileSubs = new ushort[tCount], TilePt = new byte[tCount],
            WaterType = waterType,
            TerrainGroups = []
        };
    }
}
