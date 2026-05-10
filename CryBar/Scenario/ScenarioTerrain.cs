using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace CryBar.Scenario;

public sealed class TerrainTextureGroup
{
    public required string Name { get; init; }
    public required string[] Textures { get; init; }
}

public sealed class ScenarioTerrain
{
    public required int MapSizeX { get; init; }
    public required int MapSizeZ { get; init; }
    public required float[] Heights { get; init; }
    public required float[] WaterHeights { get; init; }
    public required float[] UnkHeights { get; init; }
    public required byte[] TileGroups { get; init; }
    public required ushort[] TileSubs { get; init; }
    public required byte[] TilePt { get; init; }
    // 255 = "no water" sentinel; other values index into WaterNames. Authoritative
    // is-this-tile-water flag (WaterHeights alone bleeds into low-lying terrain).
    public required byte[] WaterType { get; init; }

    // Settable: inspector's terrain picker appends new entries from the game-wide
    // list. Append-only; existing entries are never reindexed (orphans are harmless).
    public required TerrainTextureGroup[] TerrainGroups { get; set; }

    // Round-trip metadata. Defaults emit a minimal empty TN section so synthetic
    // fixtures don't need to set them.
    public byte HasT3 { get; init; } = 1;
    public byte HasTm { get; init; }
    public uint T3Magic { get; init; }
    public uint TerrainGroupsMagic { get; init; } = 1;
    public float UnkFloat0 { get; init; }
    public float UnkFloat1 { get; init; }
    public string TileGroupsMarker { get; init; } = "TT";
    public string TileSubsMarker { get; init; } = "TS";
    public string TilePtMarker { get; init; } = "PT";
    public string WaterTypeMarker { get; init; } = "WT";

    // Opaque sections preserved verbatim for byte-exact round-trip. WaterColors
    // and WaterNames include their marker + u32 size header; TmSection is the
    // body only (emitted only when HasTm != 0). Empty = "do not emit".
    public byte[] WaterColorsSection { get; init; } = [];
    public byte[] WaterNamesSection { get; init; } = [];
    public byte[] T3Tail { get; init; } = [];
    public byte[] TmSection { get; init; } = [];
    public byte[] TnTrail { get; init; } = [];

    public static ScenarioTerrain? TryParse(ScenarioFile scenario)
    {
        if (scenario is null || !scenario.Parsed) return null;

        var j1 = scenario.GetJ1();
        if (j1 is null || !j1.Parsed) return null;

        ScenarioSection? tn = null;
        foreach (var sub in j1.Sections!) if (sub.Marker == "TN") { tn = sub; break; }
        if (tn is null) return null;

        return ParseTn(tn.Data.AsSpan());
    }

    static ScenarioTerrain? ParseTn(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return null;
        int off = 0;
        var hasT3 = data[off++];
        if (hasT3 == 0) return null; // hasT3 must be set

        if (off + 6 > data.Length) return null;
        off += 2; // 'T3'
        var t3Size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
        off += 4;
        if (off + (int)t3Size > data.Length) return null;
        var t3 = data.Slice(off, (int)t3Size);
        off += (int)t3Size;

        // Outer TN tail: optional hasTm flag + optional TM sub-section + opaque trail.
        byte hasTm = 0;
        byte[] tmSection = [];
        byte[] tnTrail = [];
        if (off < data.Length)
        {
            hasTm = data[off++];
        }
        if (hasTm != 0 && off + 6 <= data.Length)
        {
            off += 2; // 'TM'
            var tmSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
            off += 4;
            if (off + tmSize <= data.Length)
            {
                tmSection = data.Slice(off, tmSize).ToArray();
                off += tmSize;
            }
        }
        if (off < data.Length)
            tnTrail = data[off..].ToArray();

        return ParseT3(t3, hasT3, hasTm, tmSection, tnTrail);
    }

    static ScenarioTerrain? ParseT3(ReadOnlySpan<byte> t3, byte hasT3, byte hasTm, byte[] tmSection, byte[] tnTrail)
    {
        int off = 0;
        if (off + 4 > t3.Length) return null;
        var t3Magic = BinaryPrimitives.ReadUInt32LittleEndian(t3.Slice(off));
        off += 4;

        // TT terrain groups sub-section
        if (off + 6 > t3.Length) return null;
        off += 2; // 'TT'
        var ttSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(t3.Slice(off));
        off += 4;
        if (off + ttSize > t3.Length) return null;
        var ttBody = t3.Slice(off, ttSize);
        var ttMagic = ttBody.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(ttBody) : 1u;
        var groups = ParseTerrainGroups(ttBody);
        off += ttSize;

        // The two map-size u32s are stored as (gameZ, gameX) -- the file's first
        // dimension is the game's Z (north-south) axis and the second is X. Loading
        // them in the natural order with that labeling means the per-vertex/per-tile
        // arrays that follow are already in the renderer's expected
        // [vz_outer * (mapX+1) + vx_inner] layout, no transpose needed.
        if (off + 8 > t3.Length) return null;
        var mapZ = (int)BinaryPrimitives.ReadUInt32LittleEndian(t3.Slice(off));
        var mapX = (int)BinaryPrimitives.ReadUInt32LittleEndian(t3.Slice(off + 4));
        off += 8;

        if (off + 8 > t3.Length) return null;
        var unkF0 = BitConverter.ToSingle(t3.Slice(off, 4));
        var unkF1 = BitConverter.ToSingle(t3.Slice(off + 4, 4));
        off += 8;

        var tileGroupsMarker = ScenarioFile.ReadMarker(t3, off);
        var tileGroups = ReadList<byte>(t3, ref off);
        var tileSubsMarker = ScenarioFile.ReadMarker(t3, off);
        var tileSubs = ReadList<ushort>(t3, ref off);
        var tilePtMarker = ScenarioFile.ReadMarker(t3, off);
        var tilePt = ReadList<byte>(t3, ref off);

        var waterColorsSection = ReadFullSizeSection(t3, ref off);
        var waterNamesSection = ReadFullSizeSection(t3, ref off);

        var waterTypeMarker = ScenarioFile.ReadMarker(t3, off);
        var waterType = ReadList<byte>(t3, ref off);

        if (off + 4 > t3.Length) return null;
        var heightCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(t3.Slice(off));
        off += 4;
        var heights = ReadFloats(t3, ref off, heightCount);
        var waterHeights = ReadFloats(t3, ref off, heightCount);
        var unkHeights = ReadFloats(t3, ref off, heightCount);

        var t3Tail = off < t3.Length ? t3.Slice(off).ToArray() : [];

        return new ScenarioTerrain
        {
            MapSizeX = mapX,
            MapSizeZ = mapZ,
            Heights = heights,
            WaterHeights = waterHeights,
            UnkHeights = unkHeights,
            TileGroups = tileGroups,
            TileSubs = tileSubs,
            TilePt = tilePt,
            WaterType = waterType,
            TerrainGroups = groups,
            HasT3 = hasT3,
            HasTm = hasTm,
            T3Magic = t3Magic,
            TerrainGroupsMagic = ttMagic,
            UnkFloat0 = unkF0,
            UnkFloat1 = unkF1,
            TileGroupsMarker = tileGroupsMarker,
            TileSubsMarker = tileSubsMarker,
            TilePtMarker = tilePtMarker,
            WaterTypeMarker = waterTypeMarker,
            WaterColorsSection = waterColorsSection,
            WaterNamesSection = waterNamesSection,
            T3Tail = t3Tail,
            TmSection = tmSection,
            TnTrail = tnTrail,
        };
    }

    static TerrainTextureGroup[] ParseTerrainGroups(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return [];
        int off = 4; // skip ttMagic
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
        off += 4;

        var result = new TerrainTextureGroup[count];
        for (uint g = 0; g < count; g++)
        {
            if (!ScenarioFile.TryReadUTF16(data, off, out var name, out off))
                return result.AsSpan(0, (int)g).ToArray();
            if (off + 4 > data.Length)
                return result.AsSpan(0, (int)g).ToArray();
            var texCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
            off += 4;

            var textures = new string[texCount];
            uint actualTex = 0;
            for (uint t = 0; t < texCount; t++)
            {
                if (!ScenarioFile.TryReadUTF16(data, off, out var tex, out off)) break;
                textures[t] = tex;
                actualTex++;
            }
            if (actualTex < texCount)
                textures = textures.AsSpan(0, (int)actualTex).ToArray();

            result[g] = new TerrainTextureGroup { Name = name, Textures = textures };
        }
        return result;
    }

    static unsafe T[] ReadList<T>(ReadOnlySpan<byte> data, ref int off) where T : unmanaged
    {
        if (off + 6 > data.Length) return [];
        off += 2;
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
        off += 4;
        if (off + size > data.Length || size < 4) return [];
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
        int payloadBytes = Math.Min(count * sizeof(T), size - 4);
        var src = MemoryMarshal.Cast<byte, T>(data.Slice(off + 4, payloadBytes));
        var result = new T[count];
        src.Slice(0, Math.Min(src.Length, count)).CopyTo(result);
        off += size;
        return result;
    }

    static void SkipSizeSection(ReadOnlySpan<byte> data, ref int off)
    {
        if (off + 6 > data.Length) return;
        off += 2;
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
        off += 4 + size;
    }

    /// <summary>
    /// Reads a sub-section verbatim including its 2-byte marker and u32 size header.
    /// Returns the full sub-section bytes (marker + size + body) and advances the offset.
    /// Used to round-trip cosmetic sub-sections we don't model semantically.
    /// </summary>
    static byte[] ReadFullSizeSection(ReadOnlySpan<byte> data, ref int off)
    {
        if (off + 6 > data.Length) return [];
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off + 2));
        var total = 6 + size;
        if (off + total > data.Length) return [];
        var bytes = data.Slice(off, total).ToArray();
        off += total;
        return bytes;
    }

    static float[] ReadFloats(ReadOnlySpan<byte> data, ref int off, int count)
    {
        var result = new float[count];
        var available = Math.Min(count * 4, data.Length - off);
        if (available >= 4)
        {
            var src = MemoryMarshal.Cast<byte, float>(data.Slice(off, available & ~3));
            src.CopyTo(result);
        }
        off += available;
        return result;
    }

}
