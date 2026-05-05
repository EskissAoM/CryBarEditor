using System.Buffers.Binary;
using System.Xml;

namespace CryBar.Scenario;

/// <summary>
/// Parses .trg trigger export files (Age of Mythology: Retold).
/// A .trg file is: [2-byte "TR" marker][4-byte uint32 LE size][size bytes of trigger data].
/// The trigger data uses the same format as the TR section inside .mythscn scenario files,
/// except the header omits the zero1/zero2 fields (16-byte header vs 24-byte).
/// The parser auto-detects the header format so both variants are handled transparently.
/// </summary>
public class TriggerFile
{
    public bool Parsed { get; }
    public ScenarioSection? Section { get; private set; }

    public TriggerFile(ReadOnlyMemory<byte> data)
    {
        Parsed = Parse(data.Span);
    }

    TriggerFile(ScenarioSection section)
    {
        Section = section;
        Parsed = true;
    }

    bool Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 22) return false;

        // Validate "TR" marker
        if (data[0] != (byte)'T' || data[1] != (byte)'R') return false;

        var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2, 4));
        if (size > ScenarioFile.MaxSectionSize || 6 + size > (uint)data.Length) return false;

        var sectionData = data.Slice(6, (int)size).ToArray();

        if (!ScenarioFile.CanParseTr(sectionData, out ScenarioFile.TrFormat _)) return false;

        Section = new ScenarioSection("TR", sectionData);
        return true;
    }

    public string ToXml()
    {
        if (!Parsed || Section == null)
            throw new InvalidOperationException("Cannot convert unparsed trigger file");

        return ScenarioFile.SectionToTriggersXml(Section);
    }

    public byte[] ToBytes()
    {
        if (!Parsed || Section == null)
            throw new InvalidOperationException("Cannot serialize unparsed trigger file");

        return WrapSection(Section);
    }

    public static TriggerFile FromXml(string xml)
    {
        var readerSettings = new XmlReaderSettings { IgnoreWhitespace = true };
        using var reader = XmlReader.Create(new StringReader(xml), readerSettings);
        reader.MoveToContent();

        var section = ScenarioFile.ReadTrXml(reader, hasScenarioPrefix: false);
        return new TriggerFile(section);
    }

    /// <summary>
    /// Creates a standalone .trg file from a scenario's TR section by stripping
    /// the scenario-only header (scriptCount + scripts/zero2) directly in binary.
    /// </summary>
    public static TriggerFile FromScenarioSection(ScenarioSection trSection)
    {
        if (!ScenarioFile.CanParseTr(trSection.Data, out ScenarioFile.TrFormat format))
            throw new InvalidOperationException("Invalid TR section data");

        if (!format.IsScenario)
        {
            // Already in standalone format
            return new TriggerFile(new ScenarioSection("TR", trSection.Data));
        }

        // Standalone format: version + unk0 + unk1 + unk2 + (rest of body)
        // Drop everything between version and unk0 (script list or legacy zero2).
        var src = trSection.Data;
        int prefixSkip = format.BodyStart - 16;
        var dst = new byte[src.Length - prefixSkip];
        src.AsSpan(0, 4).CopyTo(dst); // version
        src.AsSpan(format.BodyStart - 12).CopyTo(dst.AsSpan(4)); // unk0,unk1,unk2 + triggerCount + body
        return new TriggerFile(new ScenarioSection("TR", dst));
    }

    static byte[] WrapSection(ScenarioSection section)
    {
        var result = new byte[6 + section.Data.Length];
        result[0] = (byte)'T';
        result[1] = (byte)'R';
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2, 4), (uint)section.Data.Length);
        section.Data.CopyTo(result, 6);
        return result;
    }
}
