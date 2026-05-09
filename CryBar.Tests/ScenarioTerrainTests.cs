using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioTerrainTests
{
    [Fact]
    public void TryParse_TestFixture_HasExpectedShape()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        var scenario = new ScenarioFile(decompressed);

        var terrain = ScenarioTerrain.TryParse(scenario);

        Assert.NotNull(terrain);
        Assert.Equal(128, terrain!.MapSizeX);
        Assert.Equal(128, terrain.MapSizeZ);
        Assert.Equal(129 * 129, terrain.Heights.Length);
        Assert.Equal(129 * 129, terrain.WaterHeights.Length);
        Assert.Equal(128 * 128, terrain.TileGroups.Length);
        Assert.Equal(128 * 128, terrain.TileSubs.Length);
        Assert.True(terrain.TerrainGroups.Length > 0, "TerrainGroups should not be empty");
        Assert.Contains(terrain.TerrainGroups, g => g.Name == "PassableLand");
    }

    [Fact]
    public void TryParse_NullScenario_ReturnsNull()
    {
        Assert.Null(ScenarioTerrain.TryParse(null!));
    }
}
