using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioFileFlushAndSaveTests
{
    static byte[] LoadFixtureRawBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        return decompressed!;
    }

    [Fact]
    public void FlushParsedViews_NoEdits_BytesByteEqualToInput()
    {
        var raw = LoadFixtureRawBytes();
        var scenario = new ScenarioFile(raw);
        Assert.True(scenario.Parsed);

        var terrain = ScenarioTerrain.TryParse(scenario);
        Assert.NotNull(terrain);

        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        scenario.FlushParsedViews(terrain!, entities);

        var rewritten = scenario.ToBytes();

        Assert.True(raw.AsSpan().SequenceEqual(rewritten),
            $"Flush+ToBytes mismatch: original={raw.Length} rewritten={rewritten.Length}");
    }
}
