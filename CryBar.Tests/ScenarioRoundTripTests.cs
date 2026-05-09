using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

[Collection("Integration")]
public class ScenarioRoundTripTests
{
    static string GamePath =>
        Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";

    [Fact]
    public void RoundTrip_TestFixture_BytesIdentical()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        Assert.True(File.Exists(path), $"Test fixture missing: {path}");
        AssertRoundTrip(path);
    }

    [SkippableFact]
    public void RoundTrip_AllCampaignScenarios_BytesIdentical()
    {
        var campaignDir = Path.Combine(GamePath, "campaign");
        Skip.IfNot(Directory.Exists(campaignDir), $"Campaign folder not found: {campaignDir}");

        var scenarios = Directory.GetFiles(campaignDir, "*.mythscn", SearchOption.AllDirectories);
        Skip.If(scenarios.Length == 0, "No campaign scenarios found");

        var failures = new List<string>();
        foreach (var path in scenarios)
        {
            try
            {
                AssertRoundTrip(path);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count}/{scenarios.Length} scenarios failed round-trip:\n" +
            string.Join("\n", failures.Take(10)) +
            (failures.Count > 10 ? $"\n... and {failures.Count - 10} more" : ""));
    }

    static void AssertRoundTrip(string scenarioPath)
    {
        var compressedBytes = File.ReadAllBytes(scenarioPath);
        var decompressed = BarCompression.DecompressL33t(compressedBytes);
        Assert.NotNull(decompressed);

        var scenario = new ScenarioFile(decompressed);
        Assert.True(scenario.Parsed, $"Failed to parse {Path.GetFileName(scenarioPath)}");

        var roundTripped = scenario.ToBytes();

        Assert.True(decompressed.AsSpan().SequenceEqual(roundTripped),
            $"Round-trip mismatch in {Path.GetFileName(scenarioPath)} " +
            $"(lengths {decompressed.Length} vs {roundTripped.Length})");
    }
}
