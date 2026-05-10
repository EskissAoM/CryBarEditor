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

    [Fact]
    public void FlushParsedViews_NoEdits_WithProtoTable_BytesByteEqualToInput()
    {
        // Same as the no-edit test but exercises the protoTable code path with
        // the unmodified table -- must still produce byte-identical output. This
        // is the key signal that WriteTmStrings reproduces ReadTmStrings exactly.
        var raw = LoadFixtureRawBytes();
        var scenario = new ScenarioFile(raw);
        Assert.True(scenario.Parsed);

        var terrain = ScenarioTerrain.TryParse(scenario);
        Assert.NotNull(terrain);

        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // Read the existing TM table and pass it through unchanged.
        var j1 = scenario.GetJ1()!;
        List<string> protoTable = [];
        foreach (var sub in j1.Sections!)
        {
            if (sub.Marker == "TM" || sub.Marker == "PT")
            {
                protoTable = ScenarioFile.ReadTmStrings(sub.Data);
                break;
            }
        }
        Assert.NotEmpty(protoTable);

        scenario.FlushParsedViews(terrain!, entities, protoTable);
        var rewritten = scenario.ToBytes();

        Assert.True(raw.AsSpan().SequenceEqual(rewritten),
            $"Flush+ToBytes (with unchanged protoTable) mismatch: original={raw.Length} rewritten={rewritten.Length}");
    }

    [Fact]
    public void FlushParsedViews_AppendsNewProtoNameToTm()
    {
        var raw = LoadFixtureRawBytes();
        var scenario = new ScenarioFile(raw);
        Assert.True(scenario.Parsed);

        var terrain = ScenarioTerrain.TryParse(scenario);
        Assert.NotNull(terrain);
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        // Read the existing TM table; append a fake proto and point an entity at it.
        var j1 = scenario.GetJ1()!;
        List<string> protoTable = [];
        foreach (var sub in j1.Sections!)
        {
            if (sub.Marker == "TM" || sub.Marker == "PT")
            {
                protoTable = ScenarioFile.ReadTmStrings(sub.Data);
                break;
            }
        }
        Assert.NotEmpty(protoTable);

        const string FakeName = "test_appended_proto";
        Assert.DoesNotContain(FakeName, protoTable);
        int newIndex = protoTable.Count;
        protoTable.Add(FakeName);

        // Mutate the first entity to point at the new proto.
        var entitiesList = entities.ToList();
        entitiesList[0].ProtoIndex = newIndex;
        entitiesList[0].ProtoName = FakeName;

        scenario.FlushParsedViews(terrain!, entitiesList, protoTable);
        var rewritten = scenario.ToBytes();

        // Reparse and confirm both the TM extension and the entity's reference
        // survived the round-trip.
        var reparsed = new ScenarioFile(rewritten);
        Assert.True(reparsed.Parsed);

        var rJ1 = reparsed.GetJ1()!;
        List<string> reparsedTable = [];
        foreach (var sub in rJ1.Sections!)
        {
            if (sub.Marker == "TM" || sub.Marker == "PT")
            {
                reparsedTable = ScenarioFile.ReadTmStrings(sub.Data);
                break;
            }
        }
        Assert.Equal(protoTable.Count, reparsedTable.Count);
        Assert.Equal(FakeName, reparsedTable[newIndex]);

        var reparsedEntities = ScenarioEntityListBuilder.Build(reparsed);
        Assert.Equal(FakeName, reparsedEntities[0].ProtoName);
        Assert.Equal(newIndex, reparsedEntities[0].ProtoIndex);
    }
}
