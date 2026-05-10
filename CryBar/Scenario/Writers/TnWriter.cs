using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace CryBar.Scenario.Writers;

/// <summary>
/// Emits the body bytes of a TN (terrain) sub-section from a typed
/// <see cref="ScenarioTerrain"/>. Output is byte-equivalent to the binary half
/// of the existing FromXml path in <c>ScenarioFile.Terrain.cs</c>, given
/// equivalent inputs.
///
/// The returned bytes are the inner TN body only (no outer "TN" marker or u32
/// length). Wrap them in a <see cref="ScenarioSection"/> if you need the full
/// sub-section form.
///
/// TN body layout (matches existing emitter):
///   u8 hasT3
///   if hasT3:
///     "T3" + u32 t3Size + t3_body
///   u8 hasTm                       (only when at least one byte remains after T3)
///   if hasTm:
///     "TM" + u32 tmSize + tmSection
///   tn_trail bytes                 (opaque, e.g. 2-byte trailer in vanilla)
///
/// t3_body layout:
///   u32 t3Magic
///   "TT" + u32 ttSize + tt_body                    (terrain-groups sub-section)
///   u32 mapZ, u32 mapX                             (file-order is gameZ then gameX)
///   f32 unkFloat0, f32 unkFloat1
///   tile_groups_marker[2] + u32 size + (u32 count + u8[count])
///   tile_subs_marker[2]   + u32 size + (u32 count + u16[count])
///   tile_pt_marker[2]     + u32 size + (u32 count + u8[count])
///   water_colors_section_bytes                     (opaque, marker + size + body)
///   water_names_section_bytes                      (opaque, marker + size + body)
///   water_type_marker[2]  + u32 size + (u32 count + u8[count])
///   u32 heightCount + f32[heightCount] x 3         (heights, waterHeights, unkHeights)
///   t3_tail bytes                                  (opaque -- CM/UM/embedded image)
///
/// tt_body layout:
///   u32 terrainGroupsMagic
///   u32 groupCount
///   per group: String16 name + u32 texCount + String16[texCount]
/// </summary>
public static class TnWriter
{
    /// <summary>
    /// Writes the TN section body (inner bytes only) for the given terrain.
    /// </summary>
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

        // hasTm byte is only present when the source TN has any bytes after the T3
        // sub-section. Vanilla scenarios always do; the empty/header-only case
        // (HasT3 == 0 and no trailing data) skips this byte entirely. We honor that
        // by only emitting hasTm when HasT3 is set OR trailing data exists.
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

        // t3Magic
        BinaryPrimitives.WriteUInt32LittleEndian(u32, terrain.T3Magic);
        ms.Write(u32);

        // TT terrain-groups sub-section
        WriteSubSection(ms, "TT", BuildTerrainGroupsBody(terrain.TerrainGroupsMagic, terrain.TerrainGroups));

        // MapSize stored as (mapZ, mapX) -- file order
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)terrain.MapSizeZ);
        ms.Write(u32);
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)terrain.MapSizeX);
        ms.Write(u32);

        // Two unknown floats
        BinaryPrimitives.WriteSingleLittleEndian(u32, terrain.UnkFloat0);
        ms.Write(u32);
        BinaryPrimitives.WriteSingleLittleEndian(u32, terrain.UnkFloat1);
        ms.Write(u32);

        // Per-tile size-list sub-sections.
        WriteSizeList(ms, terrain.TileGroupsMarker, MemoryMarshal.AsBytes(terrain.TileGroups.AsSpan()), terrain.TileGroups.Length);
        WriteSizeList(ms, terrain.TileSubsMarker, MemoryMarshal.AsBytes(terrain.TileSubs.AsSpan()), terrain.TileSubs.Length);
        WriteSizeList(ms, terrain.TilePtMarker, MemoryMarshal.AsBytes(terrain.TilePt.AsSpan()), terrain.TilePt.Length);

        // Opaque water-color / water-name sub-sections (already include marker + size header).
        if (terrain.WaterColorsSection.Length > 0)
            ms.Write(terrain.WaterColorsSection, 0, terrain.WaterColorsSection.Length);
        if (terrain.WaterNamesSection.Length > 0)
            ms.Write(terrain.WaterNamesSection, 0, terrain.WaterNamesSection.Length);

        WriteSizeList(ms, terrain.WaterTypeMarker, MemoryMarshal.AsBytes(terrain.WaterType.AsSpan()), terrain.WaterType.Length);

        // Heights: u32 count followed by three count-many float arrays. The count is
        // shared across Heights/WaterHeights/UnkHeights and only emitted once.
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

    /// <summary>
    /// Writes a marker[2] + u32(size) + (u32(count) + payload) sub-section.
    /// <paramref name="payload"/> is the raw element bytes; <paramref name="count"/> is
    /// the element count (not byte count).
    /// </summary>
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
