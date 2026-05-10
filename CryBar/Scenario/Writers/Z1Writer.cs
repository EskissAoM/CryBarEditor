using System.Buffers.Binary;

namespace CryBar.Scenario.Writers;

/// <summary>
/// Emits the body bytes of a Z1 (entities) sub-section from a typed list of
/// <see cref="ScenarioEntity"/>. Output is byte-equivalent to the binary half
/// of the existing FromXml path in <c>ScenarioFile.Entities.cs</c>, given
/// equivalent inputs.
///
/// The returned bytes are the inner Z1 body only (no outer "Z1" marker or u32
/// length). Wrap them in a <see cref="ScenarioSection"/> if you need the
/// full sub-section form.
///
/// Z1 body layout (matches existing emitter):
///   u32 entity_count
///   u8  version (a single byte; copied verbatim from the source on load)
///   per-entity record:
///     u16 entity_id
///     u16 flags (0 unless modeled in the future)
///     "H1" + u32 h1_size + h1_body
///   h1_body:
///     "EN" + u32 en_size + en_body
///     H1Suffix bytes
///   en_body:
///     H1Prefix bytes (with PlayerId patched at offset 8 as u32 LE)
///     Position 12B as (gameZ, gameY, gameX) LE floats
///     Rotation matrix 9 LE floats (36B) in row-major order
///     H1EnTail bytes (0 or 1 byte)
///
/// ProtoIndex is patched into UnitP1's name_index and name_index_copy fields
/// inside H1Suffix. New-format scenarios place UnitP1 behind a "P1" marker so
/// the patch lands at H1Suffix[6..14]; old-format scenarios inline UnitP1 at
/// H1Suffix[0..8].
/// </summary>
public static class Z1Writer
{
    /// <summary>
    /// Writes the Z1 section body (inner bytes only) for the given entities.
    /// </summary>
    /// <param name="entities">Entities to serialize, in file order.</param>
    /// <param name="protoNames">Proto-name table from the first TM/PT section.
    /// Currently unused for byte production (each entity carries its own
    /// ProtoIndex which is patched into UnitP1) but accepted for API parity
    /// with the planned writer signature.</param>
    public static byte[] Write(IReadOnlyList<ScenarioEntity> entities, IReadOnlyList<string> protoNames)
    {
        ArgumentNullException.ThrowIfNull(entities);

        // Default the version byte to 0. Real scenarios round-trip via the
        // overload that accepts the original version byte; the no-version
        // overload exists for synthetic-fixture writers/tests where the byte
        // is not separately tracked.
        return Write(entities, protoNames, version: 0);
    }

    /// <summary>
    /// Writes the Z1 section body (inner bytes only) using a caller-supplied
    /// version byte. Used by the FromXml refactor to preserve the parsed
    /// version on round-trip.
    /// </summary>
    public static byte[] Write(IReadOnlyList<ScenarioEntity> entities, IReadOnlyList<string> protoNames, byte version)
        => Write(entities, protoNames, version, entityFlags: null);

    /// <summary>
    /// Full-form overload accepting per-entity 16-bit flags. <see cref="ScenarioEntity"/>
    /// does not currently model the flags word in the per-entity envelope; callers
    /// that must preserve non-zero flags (e.g. the FromXml refactor) pass them here
    /// in parallel with <paramref name="entities"/>. <c>null</c> defaults all flags
    /// to 0, which matches every vanilla scenario observed so far.
    /// </summary>
    public static byte[] Write(
        IReadOnlyList<ScenarioEntity> entities,
        IReadOnlyList<string> protoNames,
        byte version,
        IReadOnlyList<ushort>? entityFlags)
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (entityFlags is not null && entityFlags.Count != entities.Count)
            throw new ArgumentException("entityFlags length must match entities length", nameof(entityFlags));

        // Pre-size: u32 count + 1 byte version + per-entity envelope.
        // Envelope = 4 (id+flags) + 6 (H1 marker+size) + h1Size where
        // h1Size = 6 (EN marker+size) + enSize + suffixLen.
        // enSize = prefix + 12 (pos) + 36 (rot) + tail.
        long estimate = 5;
        for (int i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            estimate += 4 + 6 + 6 + e.H1Prefix.Length + 12 + 36 + e.H1EnTail.Length + e.H1Suffix.Length;
        }

        using var ms = new MemoryStream(checked((int)estimate));

        // Reusable scratch buffers (allocated outside the loop).
        Span<byte> u32 = stackalloc byte[4];
        Span<byte> envelope = stackalloc byte[4];
        Span<byte> h1Header = stackalloc byte[6];
        h1Header[0] = (byte)'H';
        h1Header[1] = (byte)'1';

        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)entities.Count);
        ms.Write(u32);
        ms.WriteByte(version);

        for (int i = 0; i < entities.Count; i++)
        {
            var e = entities[i];

            // Per-entity envelope: u16 entityId + u16 flags. Flags are not
            // currently modeled on ScenarioEntity; default 0 unless the caller
            // supplies them explicitly.
            BinaryPrimitives.WriteUInt16LittleEndian(envelope, (ushort)e.EntityId);
            BinaryPrimitives.WriteUInt16LittleEndian(envelope.Slice(2), entityFlags is null ? (ushort)0 : entityFlags[i]);
            ms.Write(envelope);

            var h1 = BuildH1Body(e);

            // "H1" marker + u32 h1Size + h1 body.
            BinaryPrimitives.WriteUInt32LittleEndian(h1Header.Slice(2), (uint)h1.Length);
            ms.Write(h1Header);
            ms.Write(h1, 0, h1.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Builds the H1 sub-section body bytes (everything after the "H1" marker
    /// and u32 size header) for a single entity.
    /// </summary>
    static byte[] BuildH1Body(ScenarioEntity e)
    {
        // EN body: prefix (with player patched) + position (Z,Y,X) + rotation + tail.
        int enSize = e.H1Prefix.Length + 12 + 36 + e.H1EnTail.Length;
        int h1Size = 6 + enSize + e.H1Suffix.Length;

        var h1 = new byte[h1Size];
        var span = h1.AsSpan();

        // "EN" marker + u32 size.
        span[0] = (byte)'E';
        span[1] = (byte)'N';
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(2, 4), (uint)enSize);

        var enBody = span.Slice(6, enSize);

        // H1Prefix bytes verbatim, then patch PlayerId as u32 LE at offset 8.
        // (The low byte of that u32 is what the parser reads as h1[14] = h1[6+8].)
        e.H1Prefix.AsSpan().CopyTo(enBody);
        if (e.H1Prefix.Length >= 12)
            BinaryPrimitives.WriteUInt32LittleEndian(enBody.Slice(8, 4), e.PlayerId);

        // Position: file order is (gameZ, gameY, gameX). ScenarioEntity.Position
        // already maps (X=gameX, Y=gameY, Z=gameZ) per ScenarioEntityListBuilder,
        // so we emit Z, Y, X here.
        int posOff = e.H1Prefix.Length;
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(posOff, 4),     e.Position.Z);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(posOff + 4, 4), e.Position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(posOff + 8, 4), e.Position.X);

        // Rotation: 9 row-major floats (36 bytes) immediately after position.
        int rotOff = posOff + 12;
        var r = e.Rotation;
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff,      4), r.M11);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 4,  4), r.M12);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 8,  4), r.M13);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 12, 4), r.M21);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 16, 4), r.M22);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 20, 4), r.M23);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 24, 4), r.M31);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 28, 4), r.M32);
        BinaryPrimitives.WriteSingleLittleEndian(enBody.Slice(rotOff + 32, 4), r.M33);

        // H1EnTail: 0 bytes (new format) or 1 byte (old format ignore13 bool).
        if (e.H1EnTail.Length > 0)
            e.H1EnTail.AsSpan().CopyTo(enBody.Slice(rotOff + 36));

        // H1Suffix: bytes after the EN section. Patch ProtoIndex into UnitP1
        // name_index and name_index_copy (both u32 LE at the start of UnitP1).
        var suffix = span.Slice(6 + enSize, e.H1Suffix.Length);
        e.H1Suffix.AsSpan().CopyTo(suffix);
        PatchProtoIndex(suffix, e.ProtoIndex);

        return h1;
    }

    /// <summary>
    /// Patches the ProtoIndex (UnitP1 name_index and name_index_copy) into the
    /// H1Suffix bytes. In new-format scenarios the suffix begins with a "P1"
    /// marker + u32 size, so the UnitP1 inner data starts at offset 6. In old
    /// format the suffix has no marker and UnitP1 starts at offset 0.
    /// </summary>
    static void PatchProtoIndex(Span<byte> suffix, int protoIndex)
    {
        if (protoIndex < 0) return;
        var value = (uint)protoIndex;

        // Detect new-format "P1" marker prefix.
        if (suffix.Length >= 14 && suffix[0] == (byte)'P' && suffix[1] == (byte)'1')
        {
            var p1Size = BinaryPrimitives.ReadUInt32LittleEndian(suffix.Slice(2, 4));
            if (p1Size >= 8 && 6 + p1Size <= (uint)suffix.Length)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(suffix.Slice(6, 4), value);
                BinaryPrimitives.WriteUInt32LittleEndian(suffix.Slice(10, 4), value);
                return;
            }
        }

        // Old format: UnitP1 is inline at the start of the suffix.
        if (suffix.Length >= 8)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(suffix.Slice(0, 4), value);
            BinaryPrimitives.WriteUInt32LittleEndian(suffix.Slice(4, 4), value);
        }
    }
}
