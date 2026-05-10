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
    public void Build_CapturesH1SuffixBlob()
    {
        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // H1Suffix should be non-null for all and non-empty for any real H1 record
        // (UnitP1, UnitP2, markers, fake_p1 records all live after the EN section).
        Assert.All(entities, e => Assert.NotNull(e.H1Suffix));
        Assert.Contains(entities, e => e.H1Suffix.Length > 0);
    }

    [Fact]
    public void Build_CapturesH1PrefixWithKnownLayout()
    {
        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // H1Prefix length is posOff - 6 = (22 + unk_len) - 6 = 16 + unk_len.
        // unk_len is 12 (old format) or 16 (new format) -> 28 or 32 bytes.
        Assert.All(entities, e =>
            Assert.True(e.H1Prefix.Length == 28 || e.H1Prefix.Length == 32,
                $"H1Prefix.Length={e.H1Prefix.Length}, expected 28 or 32"));
    }

    [Fact]
    public void Build_CapturesH1EnTailLengthZeroOrOne()
    {
        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // H1EnTail is empty for new-format scenarios; 1 byte (ignore13 bool) for old format.
        Assert.All(entities, e =>
            Assert.True(e.H1EnTail.Length == 0 || e.H1EnTail.Length == 1,
                $"H1EnTail.Length={e.H1EnTail.Length}, expected 0 or 1"));
    }

    [Fact]
    public void Build_PlayerIdAtH1PrefixOffset8()
    {
        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // Z1Writer relies on this invariant: the u32 LE at H1Prefix[8..12] equals PlayerId.
        // h1[14] = h1[6 + 8] = H1Prefix[8] (low byte of the u32 player_id field).
        foreach (var e in entities)
        {
            Assert.True(e.H1Prefix.Length >= 12, $"H1Prefix too short: {e.H1Prefix.Length}");
            var prefixPlayerId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(e.H1Prefix.AsSpan(8, 4));
            Assert.Equal((uint)e.PlayerId, prefixPlayerId);
        }
    }
}
