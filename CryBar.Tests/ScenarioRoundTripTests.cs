using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioRoundTripTests
{
    [Fact]
    public void RoundTrip_TestFixture_BytesIdentical()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        Assert.True(File.Exists(path), $"Test fixture missing: {path}");

        var compressedBytes = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressedBytes);
        Assert.NotNull(decompressed);

        var scenario = new ScenarioFile(decompressed);
        Assert.True(scenario.Parsed, "Scenario failed to parse");

        var roundTripped = scenario.ToBytes();

        Assert.Equal(decompressed.Length, roundTripped.Length);
        Assert.True(decompressed.AsSpan().SequenceEqual(roundTripped),
            $"Round-trip mismatch (lengths {decompressed.Length} vs {roundTripped.Length})");
    }
}
