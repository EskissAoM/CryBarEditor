using CryBar.Scenario;

namespace CryBar.Tests;

public class WaterMeshBuilderTests
{
    [Fact]
    public void Build_NoWaterHeights_ReturnsNull()
    {
        var terrain = new ScenarioTerrain
        {
            MapSizeX = 2, MapSizeZ = 2,
            Heights = new float[9],
            WaterHeights = new float[0],
            UnkHeights = new float[9],
            TileGroups = new byte[4], TileSubs = new ushort[4], TilePt = new byte[4],
            TerrainGroups = []
        };

        var water = WaterMeshBuilder.Build(terrain);
        Assert.Null(water);
    }

    [Fact]
    public void Build_AllZeroWaterHeights_ReturnsNull()
    {
        var terrain = NewTerrain(waterHeights: new float[9]);
        var water = WaterMeshBuilder.Build(terrain);
        Assert.Null(water);
    }

    [Fact]
    public void Build_NonZeroWater_ReturnsPlaneAtMedianHeight()
    {
        // Three nonzero values give an unambiguous middle: sorted = [1, 3, 5], median = 3
        var heights = new float[] { 0, 1, 0, 3, 5, 0, 0, 0, 0 };
        var terrain = NewTerrain(waterHeights: heights);

        var water = WaterMeshBuilder.Build(terrain);

        Assert.NotNull(water);
        Assert.Equal(3.0f, water!.Height);
        Assert.Equal(2, water.MapSizeX);
        Assert.Equal(2, water.MapSizeZ);
    }

    static ScenarioTerrain NewTerrain(float[] waterHeights)
    {
        return new ScenarioTerrain
        {
            MapSizeX = 2, MapSizeZ = 2,
            Heights = new float[9],
            WaterHeights = waterHeights,
            UnkHeights = new float[9],
            TileGroups = new byte[4], TileSubs = new ushort[4], TilePt = new byte[4],
            TerrainGroups = []
        };
    }
}
