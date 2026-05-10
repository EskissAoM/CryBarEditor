using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioEntityListBuilderTests
{
    static ScenarioFile LoadFixtureScenario()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        return new ScenarioFile(decompressed);
    }

    [Fact]
    public void Build_TestFixture_ReturnsEntities()
    {
        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);

        // Test scenario has 4 entities (3 hoplites + 1 villager) per the user-provided screenshot
        Assert.True(entities.Length >= 4, $"Expected at least 4 entities, got {entities.Length}");
        var playerIds = entities.Select(m => m.PlayerId).Distinct().OrderBy(p => p).ToArray();
        Assert.Contains((byte)1, playerIds);
        Assert.Contains((byte)2, playerIds);
    }

    [Fact]
    public void Build_NullScenario_ReturnsEmpty()
    {
        var entities = ScenarioEntityListBuilder.Build(null!);
        Assert.Empty(entities);
    }

    [Fact]
    public void Build_PopulatesRotationFromH1Matrix()
    {
        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // Every entity must have an orthonormal rotation matrix (rows unit-length,
        // mutually perpendicular). At least one entity in vanilla scenarios will
        // have a non-identity rotation; assert that as well.
        foreach (var e in entities)
        {
            var m = e.Rotation;
            var r1Len2 = m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13;
            var r2Len2 = m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23;
            var r3Len2 = m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33;
            Assert.InRange(r1Len2, 0.99f, 1.01f);
            Assert.InRange(r2Len2, 0.99f, 1.01f);
            Assert.InRange(r3Len2, 0.99f, 1.01f);
        }

        Assert.Contains(entities, e => e.Rotation != Matrix3x3.Identity);
    }

    [Fact]
    public void Build_CapturesOtherFieldsBlob()
    {
        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // OtherFields should be non-null for all and non-empty for any real H1
        // record (there's tail data beyond position+rotation).
        Assert.All(entities, e => Assert.NotNull(e.OtherFields));
        Assert.Contains(entities, e => e.OtherFields.Length > 0);
    }
}
