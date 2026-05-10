using System.Buffers.Binary;
using System.Numerics;

namespace CryBar.Scenario;

public static class ScenarioEntityListBuilder
{
    public static ScenarioEntity[] Build(ScenarioFile scenario)
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

    static ScenarioEntity[] ParseZ1(byte[] data, List<string> protoNames)
    {
        var span = data.AsSpan();
        if (span.Length < 5) return [];

        var entityCount = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var result = new List<ScenarioEntity>((int)Math.Min(entityCount, 10000));
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
                    if (TryParseH1(sub, out var pos, out var playerId, out var protoIndex, out var rotation, out var h1Prefix, out var h1EnTail, out var h1Suffix))
                    {
                        var name = (protoIndex >= 0 && protoIndex < protoNames.Count) ? protoNames[protoIndex] : "?";
                        result.Add(new ScenarioEntity
                        {
                            Position = pos,
                            ProtoName = name,
                            ProtoIndex = protoIndex,
                            PlayerId = (byte)playerId,
                            EntityId = entityId,
                            Rotation = rotation,
                            H1Prefix = h1Prefix,
                            H1EnTail = h1EnTail,
                            H1Suffix = h1Suffix
                        });
                    }
                }

                off += 6 + (int)size;
            }
        }

        return result.ToArray();
    }

    static bool TryParseH1(
        ReadOnlySpan<byte> h1,
        out Vector3 pos,
        out int playerId,
        out int protoIndex,
        out Matrix3x3 rotation,
        out byte[] h1Prefix,
        out byte[] h1EnTail,
        out byte[] h1Suffix)
    {
        pos = default;
        playerId = 0;
        protoIndex = -1;
        rotation = Matrix3x3.Identity;
        h1Prefix = [];
        h1EnTail = [];
        h1Suffix = [];

        if (!ScenarioFile.GetEntityOffsets(h1, out int posOff, out int rotOff, out int enEnd))
            return false;

        // playerId is a single byte at offset 14, NOT an int32 -- reading 4 bytes
        // here used to pull garbage in for high values, breaking color/team grouping.
        playerId = h1[14];

        // protoIndex lives in the P1 sub-section (new format) or inline immediately
        // after EN end (old format). The unitIdCopy at offset 6 is unrelated.
        if (ScenarioFile.TryReadProtoIndex(h1, enEnd, out var pi))
            protoIndex = (int)pi;

        // The file stores position as (gameZ, gameY, gameX) -- first float is the
        // game's Z axis, third is X. Map them onto Vector3 in game-axis order so
        // Position.X means gameX downstream.
        pos = new Vector3(
            BitConverter.ToSingle(h1.Slice(posOff + 8, 4)),
            BitConverter.ToSingle(h1.Slice(posOff + 4, 4)),
            BitConverter.ToSingle(h1.Slice(posOff, 4)));

        // Rotation matrix: 9 LE floats (36 bytes) immediately after the 12-byte position.
        // Stored in game-space LH; do not reorient here. Round-trip must be byte-identical.
        if (rotOff + 36 > h1.Length) return false;
        rotation = new Matrix3x3(
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 12, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 16, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 20, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 24, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 28, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(h1.Slice(rotOff + 32, 4)));

        // H1Prefix: EN body bytes after the EN magic+size header (h1[0..5]) up to Position.
        // Contains unit_id_copy, magic zero, player_id, unk_len, unk2/unk3 padding blocks.
        h1Prefix = h1.Slice(6, posOff - 6).ToArray();

        // H1EnTail: bytes inside the EN section after the rotation matrix.
        // 0 bytes for new format; 1 byte (optional ignore13 bool) for old format.
        int tailStart = rotOff + 36;
        h1EnTail = enEnd > tailStart ? h1.Slice(tailStart, enEnd - tailStart).ToArray() : [];

        // H1Suffix: bytes after the EN section to end of H1 record. Includes UnitP1
        // (name_index = ProtoIndex), UnitP2, markers, 2x fake_p1.
        h1Suffix = enEnd < h1.Length ? h1.Slice(enEnd).ToArray() : [];
        return true;
    }
}
