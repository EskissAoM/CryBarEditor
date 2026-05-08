using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

public class EntityOverlayBuilderTests
{
    [Fact]
    public void Build_TestFixture_ReturnsMarkers()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        var scenario = new ScenarioFile(decompressed);

        var markers = EntityOverlayBuilder.Build(scenario);

        // Test scenario has 4 entities (3 hoplites + 1 villager) per the user-provided screenshot
        Assert.True(markers.Length >= 4, $"Expected at least 4 entities, got {markers.Length}");
        var playerIds = markers.Select(m => m.PlayerId).Distinct().OrderBy(p => p).ToArray();
        Assert.Contains(1, playerIds);
        Assert.Contains(2, playerIds);
    }

    [Fact]
    public void Build_NullScenario_ReturnsEmpty()
    {
        var markers = EntityOverlayBuilder.Build(null!);
        Assert.Empty(markers);
    }
}
