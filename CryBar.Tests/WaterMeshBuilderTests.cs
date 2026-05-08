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
        // 2x2 map. Set the 4 corner heights of tile (0,0) only.
        // Vertex grid is 3x3 indexed (vz * 3 + vx).
        var heights = new float[9];
        heights[0] = 5; // (vx=0, vz=0)
        heights[1] = 5; // (vx=1, vz=0)
        heights[3] = 5; // (vx=0, vz=1)
        heights[4] = 5; // (vx=1, vz=1)

        var water = WaterMeshBuilder.Build(NewTerrain(2, 2, heights));

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
    public void Build_FlatHeightAtMedianMinusBias()
    {
        // 4 watered corners @ varying heights -> median is 5, biased down by ZBias.
        // All emitted vertices share that flat height.
        var heights = new float[9];
        heights[0] = 4; heights[1] = 5; heights[3] = 5; heights[4] = 6;

        var water = WaterMeshBuilder.Build(NewTerrain(2, 2, heights))!;

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
            TerrainGroups = []
        };
    }
}
