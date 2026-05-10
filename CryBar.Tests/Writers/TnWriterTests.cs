using CryBar.Bar;
using CryBar.Scenario;
using CryBar.Scenario.Writers;

namespace CryBar.Tests.Writers;

public class TnWriterTests
{
    static ScenarioFile LoadFixtureScenario()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        return new ScenarioFile(decompressed);
    }

    static byte[] GetFixtureTnBytes(ScenarioFile scenario)
    {
        Assert.True(scenario.Parsed);
        var j1 = scenario.GetJ1();
        Assert.NotNull(j1);
        Assert.True(j1!.Parsed);

        ScenarioSection? tn = null;
        foreach (var sub in j1.Sections!) if (sub.Marker == "TN") { tn = sub; break; }
        Assert.NotNull(tn);
        return tn!.Data;
    }

    [Fact]
    public void Write_RoundTripsRealScenarioBytes()
    {
        var scenario = LoadFixtureScenario();
        var originalTn = GetFixtureTnBytes(scenario);

        var terrain = ScenarioTerrain.TryParse(scenario);
        Assert.NotNull(terrain);

        var rewritten = TnWriter.Write(terrain!);

        Assert.True(originalTn.AsSpan().SequenceEqual(rewritten),
            $"TN byte mismatch: original={originalTn.Length} rewritten={rewritten.Length}");
    }

    [Fact]
    public void FromXmlPath_PreservesTnBytes_ViaRefactoredEmitter()
    {
        // The FromXml path now routes through TnWriter; this guards the refactor by
        // serializing the fixture to XML and back, then comparing the TN sub-section
        // bytes against the original.
        var scenario = LoadFixtureScenario();
        var originalTn = GetFixtureTnBytes(scenario);

        var xml = scenario.ToXml();
        var fromXml = ScenarioFile.FromXml(xml);
        Assert.True(fromXml.Parsed);

        var j1 = fromXml.GetJ1();
        Assert.NotNull(j1);
        ScenarioSection? tn = null;
        foreach (var s in j1!.Sections!) if (s.Marker == "TN") { tn = s; break; }
        Assert.NotNull(tn);

        Assert.True(originalTn.AsSpan().SequenceEqual(tn!.Data),
            $"FromXml TN mismatch: original={originalTn.Length} rt={tn.Data.Length}");
    }
}
