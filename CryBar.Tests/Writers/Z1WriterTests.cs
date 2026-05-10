using System.Buffers.Binary;
using System.Numerics;
using CryBar.Bar;
using CryBar.Scenario;
using CryBar.Scenario.Writers;

namespace CryBar.Tests.Writers;

public class Z1WriterTests
{
    static ScenarioFile LoadFixtureScenario()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(path);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);
        return new ScenarioFile(decompressed);
    }

    static (byte[] z1Bytes, List<string> protos, byte version) GetFixtureZ1AndProtos()
    {
        var scenario = LoadFixtureScenario();
        Assert.True(scenario.Parsed);

        var j1 = scenario.GetJ1();
        Assert.NotNull(j1);
        Assert.True(j1!.Parsed);

        var protos = new List<string>();
        ScenarioSection? z1 = null;
        foreach (var sub in j1.Sections!)
        {
            if (protos.Count == 0 && (sub.Marker == "TM" || sub.Marker == "PT"))
                protos = ScenarioFile.ReadTmStrings(sub.Data);
            if (sub.Marker == "Z1") z1 = sub;
        }

        Assert.NotNull(z1);
        Assert.True(z1!.Data.Length >= 5);
        var version = z1.Data[4];
        return (z1.Data, protos, version);
    }

    [Fact]
    public void Write_RoundTripsRealScenarioBytes()
    {
        var (originalZ1, protos, version) = GetFixtureZ1AndProtos();

        var scenario = LoadFixtureScenario();
        var entities = ScenarioEntityListBuilder.Build(scenario);
        Assert.NotEmpty(entities);

        var rewritten = Z1Writer.Write(entities, protos, version);

        Assert.True(originalZ1.AsSpan().SequenceEqual(rewritten),
            $"Z1 byte mismatch: original={originalZ1.Length} rewritten={rewritten.Length}");
    }

    [Fact]
    public void FromXmlPath_PreservesZ1Bytes_ViaRefactoredEmitter()
    {
        // The FromXml path now routes through Z1Writer; this guards the refactor by
        // serializing the fixture to XML and back, then comparing the Z1 sub-section
        // bytes against the original.
        var (originalZ1, _, _) = GetFixtureZ1AndProtos();

        var scenario = LoadFixtureScenario();
        var xml = scenario.ToXml();
        var fromXml = ScenarioFile.FromXml(xml);
        Assert.True(fromXml.Parsed);

        var j1 = fromXml.GetJ1();
        Assert.NotNull(j1);
        ScenarioSection? z1 = null;
        foreach (var s in j1!.Sections!) if (s.Marker == "Z1") { z1 = s; break; }
        Assert.NotNull(z1);
        Assert.True(originalZ1.AsSpan().SequenceEqual(z1!.Data),
            $"FromXml Z1 mismatch: original={originalZ1.Length} rt={z1.Data.Length}");
    }

    [Fact]
    public void Write_EmptyList_ProducesValidZ1Header()
    {
        var bytes = Z1Writer.Write([], []);
        Assert.True(bytes.Length >= 5);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(0, bytes[4]);
    }

    [Fact]
    public void Write_PreservesEntityHeader()
    {
        // A handcrafted entity exercises the per-entity envelope (id + flags = 0).
        // Use minimum-size H1Prefix (28 bytes) and no suffix so only the EN header
        // structure is tested, independent of UnitP1 semantics.
        var prefix = new byte[28];
        // unk_len at offset 12 must equal 12 for new-format prefix length 28
        // (parser checks: posOff = 22 + unk_len, and prefix length = posOff - 6).
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(12, 4), 12);

        var entity = new ScenarioEntity
        {
            EntityId = 0x1234,
            ProtoIndex = 5,
            ProtoName = "villager",
            PlayerId = 7,
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Matrix3x3.Identity,
            H1Prefix = prefix,
            H1EnTail = [],
            H1Suffix = []
        };

        var bytes = Z1Writer.Write([entity], []);

        // u32 count + 1 byte version
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(0, bytes[4]);

        // Per-entity envelope: u16 id, u16 flags
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(7)));

        // "H1" marker
        Assert.Equal((byte)'H', bytes[9]);
        Assert.Equal((byte)'1', bytes[10]);
        var h1Size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(11));

        // h1Size == 6 (EN hdr) + enSize + 0 suffix; enSize == 28 + 12 + 36 = 76.
        Assert.Equal((uint)(6 + 76), h1Size);

        // "EN" marker
        Assert.Equal((byte)'E', bytes[15]);
        Assert.Equal((byte)'N', bytes[16]);
        Assert.Equal(76u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(17)));

        // PlayerId patched at H1Prefix[8..12] => bytes offset 29 (15 + 6 + 8).
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(29)));

        // Position written as (Z, Y, X) in file order at offset (15 + 6 + 28) = 49.
        Assert.Equal(3f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(49))); // Z
        Assert.Equal(2f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(53))); // Y
        Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(57))); // X
    }

    [Fact]
    public void Write_PatchesProtoIndex_NewFormatSuffix()
    {
        // Suffix starts with "P1" marker followed by u32 size and UnitP1 inner data
        // beginning with name_index + name_index_copy. Z1Writer must patch both u32s.
        var suffix = new byte[6 + 16];
        suffix[0] = (byte)'P';
        suffix[1] = (byte)'1';
        BinaryPrimitives.WriteUInt32LittleEndian(suffix.AsSpan(2, 4), 16);
        // pre-populate with sentinel; Z1Writer should overwrite indices 6..14
        for (int i = 6; i < suffix.Length; i++) suffix[i] = 0xAA;

        var prefix = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(12, 4), 12);

        var entity = new ScenarioEntity
        {
            EntityId = 1,
            ProtoIndex = 0xCAFE,
            ProtoName = "x",
            PlayerId = 0,
            Position = Vector3.Zero,
            Rotation = Matrix3x3.Identity,
            H1Prefix = prefix,
            H1EnTail = [],
            H1Suffix = suffix
        };

        var bytes = Z1Writer.Write([entity], []);

        // Suffix begins immediately after the EN section. Layout up to suffix:
        //   5 (z1 hdr) + 4 (id+flags) + 6 (H1 hdr) + 6 (EN hdr) + 76 (en body) = 97
        int suffixStart = 5 + 4 + 6 + 6 + (28 + 12 + 36);
        Assert.Equal((byte)'P', bytes[suffixStart]);
        Assert.Equal((byte)'1', bytes[suffixStart + 1]);
        Assert.Equal(0xCAFEu, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(suffixStart + 6)));
        Assert.Equal(0xCAFEu, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(suffixStart + 10)));
        // Bytes after the patched u32s remain at sentinel
        Assert.Equal(0xAA, bytes[suffixStart + 14]);
        Assert.Equal(0xAA, bytes[suffixStart + 15]);
    }

    [Fact]
    public void Write_PatchesProtoIndex_OldFormatSuffix()
    {
        // Old-format: suffix has no "P1" marker; UnitP1 is inlined at offset 0.
        // Use a leading byte that isn't ASCII printable so the writer's marker
        // sniff falls through to the inline path.
        var suffix = new byte[16];
        for (int i = 0; i < suffix.Length; i++) suffix[i] = 0xAA;
        suffix[0] = 0x00; // not ASCII printable, but the writer detects via "P1" pattern

        var prefix = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(12, 4), 12);

        var entity = new ScenarioEntity
        {
            EntityId = 1,
            ProtoIndex = 0xBEEF,
            ProtoName = "x",
            PlayerId = 0,
            Position = Vector3.Zero,
            Rotation = Matrix3x3.Identity,
            H1Prefix = prefix,
            H1EnTail = [],
            H1Suffix = suffix
        };

        var bytes = Z1Writer.Write([entity], []);
        int suffixStart = 5 + 4 + 6 + 6 + (28 + 12 + 36);

        Assert.Equal(0xBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(suffixStart)));
        Assert.Equal(0xBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(suffixStart + 4)));
        // Bytes 8..16 stay at sentinel
        for (int i = 8; i < 16; i++)
            Assert.Equal(0xAA, bytes[suffixStart + i]);
    }
}
