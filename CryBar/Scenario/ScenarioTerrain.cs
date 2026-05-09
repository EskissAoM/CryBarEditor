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
    /// <summary>
    /// Per-tile water marker. 0 = water tile (uses the default body), non-zero =
    /// "no-water" override the editor stamps on dry holes / non-water depressions.
    /// This is the authoritative "is this tile water?" flag -- WaterHeights alone
    /// can't be trusted because the sea-level reference bleeds into low-lying
    /// non-water terrain. WaterMeshBuilder reads it as `value == 0`.
    /// </summary>
    public required byte[] WaterType { get; init; }
    public required TerrainTextureGroup[] TerrainGroups { get; init; }

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
        if (data[off++] == 0) return null; // hasT3 must be set

        if (off + 6 > data.Length) return null;
        off += 2; // 'T3'
        var t3Size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(off));
        off += 4;
        if (off + (int)t3Size > data.Length) return null;
        var t3 = data.Slice(off, (int)t3Size);

        return ParseT3(t3);
    }

    static ScenarioTerrain? ParseT3(ReadOnlySpan<byte> t3)
    {
        int off = 0;
        if (off + 4 > t3.Length) return null;
        off += 4; // t3Magic

        // TT terrain groups sub-section
        if (off + 6 > t3.Length) return null;
        off += 2; // 'TT'
        var ttSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(t3.Slice(off));
        off += 4;
        if (off + ttSize > t3.Length) return null;
        var groups = ParseTerrainGroups(t3.Slice(off, ttSize));
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

        // 2 unknown floats
        if (off + 8 > t3.Length) return null;
        off += 8;

        var tileGroups = ReadList<byte>(t3, ref off);
        var tileSubs = ReadList<ushort>(t3, ref off);
        var tilePt = ReadList<byte>(t3, ref off);

        // Skip WaterColors and WaterNames (cosmetic metadata for the water palette).
        SkipSizeSection(t3, ref off);
        SkipSizeSection(t3, ref off);

        // WaterType is per-tile: 0 = no water, non-zero = index into WaterNames.
        var waterType = ReadList<byte>(t3, ref off);

        if (off + 4 > t3.Length) return null;
        var heightCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(t3.Slice(off));
        off += 4;
        var heights = ReadFloats(t3, ref off, heightCount);
        var waterHeights = ReadFloats(t3, ref off, heightCount);
        var unkHeights = ReadFloats(t3, ref off, heightCount);

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
            TerrainGroups = groups
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
