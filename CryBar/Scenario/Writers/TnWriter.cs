using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace CryBar.Scenario.Writers;

/// Emits the TN (terrain) sub-section body. Output is byte-equivalent to the
/// FromXml binary path. Returned bytes are the inner body only.
///
/// Layout:
///   u8 hasT3
///   if hasT3: "T3" + u32 size + t3_body
///   u8 hasTm                       (only when T3 present or any trailing data)
///   if hasTm: "TM" + u32 size + tmSection
///   tn_trail bytes                 (opaque)
///
/// t3_body:
///   u32 t3Magic
///   "TT" + u32 size + (u32 magic + u32 groupCount + per-group(String16 name + u32 texCount + String16[]))
///   u32 mapZ, u32 mapX, f32 unkF0, f32 unkF1
///   3x size-list (TileGroups u8, TileSubs u16, TilePt u8)
///   waterColorsSection, waterNamesSection (opaque, marker+size+body)
///   size-list (WaterType u8)
///   u32 heightCount + f32[heightCount] x 3
///   t3_tail bytes                  (opaque -- CM/UM/embedded image)
public static class TnWriter
{
    public static byte[] Write(ScenarioTerrain terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        using var ms = new MemoryStream();

        ms.WriteByte(terrain.HasT3);

        if (terrain.HasT3 != 0)
        {
            var t3 = BuildT3Body(terrain);
            WriteSubSection(ms, "T3", t3);
        }

        // hasTm byte is only present when there's data after T3.
        var emitHasTm = terrain.HasT3 != 0 || terrain.HasTm != 0 || terrain.TmSection.Length > 0 || terrain.TnTrail.Length > 0;
        if (emitHasTm)
            ms.WriteByte(terrain.HasTm);

        if (terrain.HasTm != 0)
            WriteSubSection(ms, "TM", terrain.TmSection);

        if (terrain.TnTrail.Length > 0)
            ms.Write(terrain.TnTrail, 0, terrain.TnTrail.Length);

        return ms.ToArray();
    }

    static byte[] BuildT3Body(ScenarioTerrain terrain)
    {
        using var ms = new MemoryStream();
        Span<byte> u32 = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(u32, terrain.T3Magic);
        ms.Write(u32);

        WriteSubSection(ms, "TT", BuildTerrainGroupsBody(terrain.TerrainGroupsMagic, terrain.TerrainGroups));

        // File order: (mapZ, mapX).
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)terrain.MapSizeZ);
        ms.Write(u32);
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)terrain.MapSizeX);
        ms.Write(u32);

        BinaryPrimitives.WriteSingleLittleEndian(u32, terrain.UnkFloat0);
        ms.Write(u32);
        BinaryPrimitives.WriteSingleLittleEndian(u32, terrain.UnkFloat1);
        ms.Write(u32);

        WriteSizeList(ms, terrain.TileGroupsMarker, MemoryMarshal.AsBytes(terrain.TileGroups.AsSpan()), terrain.TileGroups.Length);
        WriteSizeList(ms, terrain.TileSubsMarker, MemoryMarshal.AsBytes(terrain.TileSubs.AsSpan()), terrain.TileSubs.Length);
        WriteSizeList(ms, terrain.TilePtMarker, MemoryMarshal.AsBytes(terrain.TilePt.AsSpan()), terrain.TilePt.Length);

        if (terrain.WaterColorsSection.Length > 0)
            ms.Write(terrain.WaterColorsSection, 0, terrain.WaterColorsSection.Length);
        if (terrain.WaterNamesSection.Length > 0)
            ms.Write(terrain.WaterNamesSection, 0, terrain.WaterNamesSection.Length);

        WriteSizeList(ms, terrain.WaterTypeMarker, MemoryMarshal.AsBytes(terrain.WaterType.AsSpan()), terrain.WaterType.Length);

        // Single shared count for Heights/WaterHeights/UnkHeights.
        var heightCount = terrain.Heights.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)heightCount);
        ms.Write(u32);
        WriteFloatArray(ms, terrain.Heights);
        WriteFloatArray(ms, terrain.WaterHeights);
        WriteFloatArray(ms, terrain.UnkHeights);

        if (terrain.T3Tail.Length > 0)
            ms.Write(terrain.T3Tail, 0, terrain.T3Tail.Length);

        return ms.ToArray();
    }

    static byte[] BuildTerrainGroupsBody(uint magic, TerrainTextureGroup[] groups)
    {
        using var ms = new MemoryStream();
        Span<byte> u32 = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(u32, magic);
        ms.Write(u32);
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)groups.Length);
        ms.Write(u32);

        foreach (var g in groups)
        {
            WriteString16(ms, g.Name);
            BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)g.Textures.Length);
            ms.Write(u32);
            foreach (var tex in g.Textures)
                WriteString16(ms, tex);
        }

        return ms.ToArray();
    }

    /// marker[2] + u32(4 + payload.Length) + u32(count) + payload.
    static void WriteSizeList(Stream stream, string marker, ReadOnlySpan<byte> payload, int count)
    {
        Span<byte> hdr = stackalloc byte[6];
        hdr[0] = (byte)marker[0];
        hdr[1] = (byte)marker[1];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(2), (uint)(4 + payload.Length));
        stream.Write(hdr);
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)count);
        stream.Write(u32);
        if (payload.Length > 0)
            stream.Write(payload);
    }

    static void WriteSubSection(Stream stream, string marker, byte[] data)
    {
        Span<byte> hdr = stackalloc byte[6];
        hdr[0] = (byte)marker[0];
        hdr[1] = (byte)marker[1];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(2), (uint)data.Length);
        stream.Write(hdr);
        if (data.Length > 0)
            stream.Write(data, 0, data.Length);
    }

    // u32 char-count + UTF-16 LE bytes.
    static void WriteString16(Stream stream, string value)
    {
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)value.Length);
        stream.Write(u32);
        if (value.Length == 0) return;
        var bytes = Encoding.Unicode.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    static void WriteFloatArray(Stream stream, float[] values)
    {
        if (values.Length == 0) return;
        var bytes = MemoryMarshal.AsBytes(values.AsSpan());
        stream.Write(bytes);
    }
}
