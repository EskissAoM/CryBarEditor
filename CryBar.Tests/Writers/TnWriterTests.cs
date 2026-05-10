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
    public void Write_AppendingTextureToExistingGroup_RoundTrips()
    {
        // Mirrors the inspector's "user picks a texture not yet in the scenario's
        // TerrainGroups" path: append a new texture to the first existing group,
        // re-emit TN bytes, reparse, confirm the new entry survived.
        var scenario = LoadFixtureScenario();
        var terrain = ScenarioTerrain.TryParse(scenario);
        Assert.NotNull(terrain);
        Assert.NotEmpty(terrain!.TerrainGroups);

        var grp0 = terrain.TerrainGroups[0];
        const string FakeTex = "test_appended_texture_in_existing_group";
        Assert.DoesNotContain(FakeTex, grp0.Textures);

        var newTexs = new string[grp0.Textures.Length + 1];
        System.Array.Copy(grp0.Textures, newTexs, grp0.Textures.Length);
        newTexs[grp0.Textures.Length] = FakeTex;
        terrain.TerrainGroups[0] = new TerrainTextureGroup { Name = grp0.Name, Textures = newTexs };

        var rewritten = TnWriter.Write(terrain);

        // Reparse the rewritten TN bytes and confirm the appended texture is there.
        var reparsed = ParseTnDirect(rewritten);
        Assert.NotNull(reparsed);
        Assert.Equal(terrain.TerrainGroups.Length, reparsed!.TerrainGroups.Length);
        Assert.Equal(grp0.Name, reparsed.TerrainGroups[0].Name);
        Assert.Equal(grp0.Textures.Length + 1, reparsed.TerrainGroups[0].Textures.Length);
        Assert.Equal(FakeTex, reparsed.TerrainGroups[0].Textures[^1]);
    }

    [Fact]
    public void Write_AppendingNewGroup_RoundTrips()
    {
        // Mirrors the inspector's "user picks a (group, texture) where the group
        // itself isn't in the scenario's TerrainGroups" path: append a brand new
        // TerrainGroup, re-emit TN bytes, reparse, confirm survived.
        var scenario = LoadFixtureScenario();
        var terrain = ScenarioTerrain.TryParse(scenario);
        Assert.NotNull(terrain);

        const string FakeGroup = "test_appended_group";
        const string FakeTex   = "test_appended_group_texture";
        Assert.DoesNotContain(terrain!.TerrainGroups, g => g.Name == FakeGroup);

        var newGroups = new TerrainTextureGroup[terrain.TerrainGroups.Length + 1];
        System.Array.Copy(terrain.TerrainGroups, newGroups, terrain.TerrainGroups.Length);
        newGroups[^1] = new TerrainTextureGroup { Name = FakeGroup, Textures = new[] { FakeTex } };
        terrain.TerrainGroups = newGroups;

        var rewritten = TnWriter.Write(terrain);

        var reparsed = ParseTnDirect(rewritten);
        Assert.NotNull(reparsed);
        Assert.Equal(terrain.TerrainGroups.Length, reparsed!.TerrainGroups.Length);
        Assert.Equal(FakeGroup, reparsed.TerrainGroups[^1].Name);
        Assert.Single(reparsed.TerrainGroups[^1].Textures);
        Assert.Equal(FakeTex, reparsed.TerrainGroups[^1].Textures[0]);
    }

    /// <summary>
    /// Reparse a freshly-emitted TN body by wrapping it in a synthetic J1 -> TN
    /// envelope so ScenarioTerrain.TryParse can consume it. Avoids re-running
    /// the full FlushParsedViews / ToBytes pipeline just to verify the writer.
    /// </summary>
    static ScenarioTerrain? ParseTnDirect(byte[] tnBody)
    {
        // ScenarioTerrain.ParseTn is private; round-trip via TryParse needs a
        // ScenarioFile. Easiest path: serialize as a J1-bearing scenario by
        // patching the fixture's TN bytes with the new ones.
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        var scenario = new ScenarioFile(decompressed!);
        var j1 = scenario.GetJ1()!;
        foreach (var sub in j1.Sections!)
        {
            if (sub.Marker == "TN") { sub.Data = tnBody; break; }
        }
        // Round-trip the J1 bytes back into the top-level scenario then reparse.
        var j1Section = scenario.FindSection("J1")!;
        j1Section.Data = j1.ToBytes();
        var rewritten = scenario.ToBytes();
        var reparsedFile = new ScenarioFile(rewritten);
        return ScenarioTerrain.TryParse(reparsedFile);
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
