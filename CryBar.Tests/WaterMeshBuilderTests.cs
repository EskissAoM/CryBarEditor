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
    public void Build_FlatHeightAtMedianMinusBias()
    {
        var heights = new float[9];
        heights[0] = 4; heights[1] = 5; heights[3] = 5; heights[4] = 6;

        var water = WaterMeshBuilder.Build(NewWaterTerrain(2, 2, heights, waterTiles: [0]))!;

        // ZBias 0.7 -> emitted Y = 5 - 0.7 = 4.3
        for (int i = 0; i < water.VertexCount; i++)
            Assert.Equal(4.3f, water.Vertices[i * 3 + 1], 4);
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
