using System.Buffers.Binary;
using System.Numerics;

namespace CryBar.Scenario;

public static class EntityOverlayBuilder
{
    public static EntityMarker[] Build(ScenarioFile scenario)
    {
        if (scenario is null || !scenario.Parsed) return [];

        var j1 = scenario.GetJ1();
        if (j1 is null || !j1.Parsed) return [];

        // First TM/PT section is the protounit name table that Z1 entities reference by index
        List<string> protoNames = [];
        foreach (var sub in j1.Sections!)
        {
            if (sub.Marker == "TM" || sub.Marker == "PT")
            {
                protoNames = ScenarioFile.ReadTmStrings(sub.Data);
                break;
            }
        }

        ScenarioSection? z1 = null;
        foreach (var sub in j1.Sections) if (sub.Marker == "Z1") { z1 = sub; break; }
        if (z1 is null) return [];

        return ParseZ1(z1.Data, protoNames);
    }

    static EntityMarker[] ParseZ1(byte[] data, List<string> protoNames)
    {
        var span = data.AsSpan();
        if (span.Length < 5) return [];

        var entityCount = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var result = new List<EntityMarker>((int)Math.Min(entityCount, 10000));
        int off = 5;

        for (uint ei = 0; ei < entityCount && off + 4 <= span.Length; ei++)
        {
            var entityId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off));
            off += 4; // id + flags

            while (off + 6 <= span.Length)
            {
                byte b0 = span[off], b1 = span[off + 1];
                if (b0 < 0x20 || b0 > 0x7E || b1 < 0x20 || b1 > 0x7E) break;

                var marker = ScenarioFile.ReadMarker(span, off);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off + 2));
                if (size > 100_000 || off + 6 + size > (uint)span.Length) break;

                if (marker == "H1" && size >= 82)
                {
                    var sub = span.Slice(off + 6, (int)size);
                    if (TryParseH1(sub, out var pos, out var playerId, out var protoIndex))
                    {
                        var name = (protoIndex >= 0 && protoIndex < protoNames.Count) ? protoNames[protoIndex] : "?";
                        result.Add(new EntityMarker
                        {
                            Position = pos,
                            ProtoName = name,
                            PlayerId = playerId,
                            EntityId = entityId
                        });
                    }
                }

                off += 6 + (int)size;
            }
        }

        return result.ToArray();
    }

    static bool TryParseH1(ReadOnlySpan<byte> h1, out Vector3 pos, out int playerId, out int protoIndex)
    {
        pos = default; playerId = 0; protoIndex = -1;
        if (h1.Length < 22) return false;

        var enSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(h1.Slice(2));
        if (6 + enSize > h1.Length || enSize < 52) return false;

        protoIndex = BinaryPrimitives.ReadInt32LittleEndian(h1.Slice(6));
        playerId = BinaryPrimitives.ReadInt32LittleEndian(h1.Slice(14));

        var unk2Len = (int)BinaryPrimitives.ReadUInt32LittleEndian(h1.Slice(18));
        if (unk2Len < 0 || unk2Len > 1000) return false;

        int posOff = 22 + unk2Len;
        if (posOff + 12 > h1.Length) return false;

        pos = new Vector3(
            BitConverter.ToSingle(h1.Slice(posOff, 4)),
            BitConverter.ToSingle(h1.Slice(posOff + 4, 4)),
            BitConverter.ToSingle(h1.Slice(posOff + 8, 4)));
        return true;
    }
}
