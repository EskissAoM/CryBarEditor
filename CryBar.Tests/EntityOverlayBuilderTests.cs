using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

public class EntityOverlayBuilderTests
{
    [Fact]
    public void Build_TestFixture_ReturnsEntities()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        var scenario = new ScenarioFile(decompressed);

        var entities = EntityOverlayBuilder.Build(scenario);

        // Test scenario has 4 entities (3 hoplites + 1 villager) per the user-provided screenshot
        Assert.True(entities.Length >= 4, $"Expected at least 4 entities, got {entities.Length}");
        var playerIds = entities.Select(m => m.PlayerId).Distinct().OrderBy(p => p).ToArray();
        Assert.Contains((byte)1, playerIds);
        Assert.Contains((byte)2, playerIds);
    }

    [Fact]
    public void Build_NullScenario_ReturnsEmpty()
    {
        var entities = EntityOverlayBuilder.Build(null!);
        Assert.Empty(entities);
    }
}
