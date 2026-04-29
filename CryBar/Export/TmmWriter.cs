using System.Buffers.Binary;
using System.Text;
using CryBar.TMM;

namespace CryBar.Export;

public static class TmmWriter
{
    public static (byte[] Tmm, byte[] TmmData, IReadOnlyList<string> Warnings) Write(GlbModel model)
    {
        var warnings = new List<string>();

        // Build .tmm.data buffer first so we know the offsets for the header.
        var data = BuildDataBuffer(model, out var dataLayout);

        var tmm = BuildTmmHeader(model, dataLayout, warnings);
        return (tmm, data, warnings);
    }

    // Offsets and byte lengths for each section inside the .tmm.data buffer.
    readonly struct DataLayout(
        uint vertStart, uint vertBytes,
        uint idxStart, uint idxBytes,
        uint heightStart, uint heightBytes)
    {
        public readonly uint VertStart   = vertStart;
        public readonly uint VertBytes   = vertBytes;
        public readonly uint IdxStart    = idxStart;
        public readonly uint IdxBytes    = idxBytes;
        public readonly uint HeightStart = heightStart;
        public readonly uint HeightBytes = heightBytes;
    }

    static byte[] BuildDataBuffer(GlbModel model, out DataLayout layout)
    {
        var primitives = model.Mesh.Primitives;

        int totalVerts = 0;
        int totalIdxCount = 0;
        foreach (var prim in primitives)
        {
            totalVerts += prim.Positions.Length / 3;
            totalIdxCount += prim.Indices.Length;
        }

        uint vertBytes   = (uint)(totalVerts    * TmmVertex.SizeInBytes);
        uint idxBytes    = (uint)(totalIdxCount * 2);
        uint heightBytes = (uint)(totalVerts    * 2);

        uint vertStart   = 0;
        uint idxStart    = vertStart + vertBytes;
        uint heightStart = idxStart + idxBytes;
        uint totalBytes  = heightStart + heightBytes;

        var buf = new byte[totalBytes];

        int vOff = (int)vertStart;
        int iOff = (int)idxStart;
        int hOff = (int)heightStart;

        foreach (var prim in primitives)
        {
            int vc = prim.Positions.Length / 3;
            for (int i = 0; i < vc; i++)
            {
                float px = prim.Positions[i * 3];
                float py = prim.Positions[i * 3 + 1];
                float pz = prim.Positions[i * 3 + 2];

                // X-negate: convert glTF Y-up RH -> TMM Y-up LH
                BinaryPrimitives.WriteHalfLittleEndian(buf.AsSpan(vOff),     (Half)(-px));
                BinaryPrimitives.WriteHalfLittleEndian(buf.AsSpan(vOff + 2), (Half)py);
                BinaryPrimitives.WriteHalfLittleEndian(buf.AsSpan(vOff + 4), (Half)pz);

                float u  = prim.TexCoords[i * 2];
                float v  = prim.TexCoords[i * 2 + 1];
                // V passes through unchanged: GlbExporter does not flip on export, so the round-trip
                // to game V (which python originally Blender-flipped via 1-V) is already encoded.
                BinaryPrimitives.WriteHalfLittleEndian(buf.AsSpan(vOff + 6), (Half)u);
                BinaryPrimitives.WriteHalfLittleEndian(buf.AsSpan(vOff + 8), (Half)v);

                float nx = prim.Normals[i * 3];
                float ny = prim.Normals[i * 3 + 1];
                float nz = prim.Normals[i * 3 + 2];

                float tx = prim.Tangents[i * 4];
                float ty = prim.Tangents[i * 4 + 1];
                float tz = prim.Tangents[i * 4 + 2];
                float tw = prim.Tangents[i * 4 + 3];

                var (px16, py16, pz16) = PackTbn(nx, ny, nz, tx, ty, tz, tw);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(vOff + 10), px16);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(vOff + 12), py16);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(vOff + 14), pz16);

                // Height buffer: game Y is the height dimension (Y-up coordinate)
                BinaryPrimitives.WriteHalfLittleEndian(buf.AsSpan(hOff), (Half)py);

                vOff += TmmVertex.SizeInBytes;
                hOff += 2;
            }

            // Triangle winding: reverse [i0, i1, i2] -> [i0, i2, i1] (RH -> LH)
            var idx = prim.Indices;
            for (int t = 0; t < idx.Length; t += 3)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(iOff),     (ushort)idx[t]);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(iOff + 2), (ushort)idx[t + 2]);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(iOff + 4), (ushort)idx[t + 1]);
                iOff += 6;
            }
        }

        layout = new DataLayout(vertStart, vertBytes, idxStart, idxBytes, heightStart, heightBytes);
        return buf;
    }

    // Packs a glTF-space (Y-up RH) normal/tangent/bitangentSign into three u16 TBN values.
    // Converts to game space (Y-up LH) by negating X, then runs Shepperd quaternion extraction.
    static (ushort X, ushort Y, ushort Z) PackTbn(
        float nx, float ny, float nz,
        float tx, float ty, float tz,
        float tw)
    {
        // Convert to game space: negate X (inverse of what GlbExporter does)
        float ngx = -nx, ngy = ny, ngz = nz;
        float tgx = -tx, tgy = ty, tgz = tz;

        // glTF tangent.w: -1 = non-mirrored (handedness=0), +1 = mirrored (handedness=1)
        int handedness = tw > 0.0f ? 1 : 0;

        // Compute game-space B from T x N (B_game = cross(T_game, N_game))
        // For mirrored faces the stored B is negated, so the cross product always
        // gives the "real" B that, when negated back per the handedness encoding, round-trips.
        float bgx = tgy * ngz - tgz * ngy;
        float bgy = tgz * ngx - tgx * ngz;
        float bgz = tgx * ngy - tgy * ngx;

        // Build proper-rotation matrix [T_g | B_g | N_g] for Shepperd.
        // Mirrored faces use -B so the matrix has det=+1 (right-handed frame),
        // with the handedness bit signaling the decoder to flip B back.
        // sb * B_g: sb=+1 for handedness=0, sb=-1 for handedness=1
        // The cross product above already gives B_g before the sb flip.
        // When handedness=1, col1 = sb*B_g = -1 * (-cross(T,N)) = cross(T,N),
        // and cross(T,N) = bgx/bgy/bgz as computed. So col1 = bgx/bgy/bgz always.
        float m00 = tgx, m10 = tgy, m20 = tgz; // col 0 = T_g
        float m01 = bgx, m11 = bgy, m21 = bgz; // col 1 = sb*B_g
        float m02 = ngx, m12 = ngy, m22 = ngz; // col 2 = N_g

        // Shepperd quaternion extraction
        float trace = m00 + m11 + m22;
        float x, y, z, w;
        if (trace > 0.0f)
        {
            float s = 0.5f / MathF.Sqrt(trace + 1.0f);
            w = 0.25f / s;
            x = (m21 - m12) * s;
            y = (m02 - m20) * s;
            z = (m10 - m01) * s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = 2.0f * MathF.Sqrt(1.0f + m00 - m11 - m22);
            x = 0.25f * s; y = (m01 + m10) / s; z = (m02 + m20) / s; w = (m21 - m12) / s;
        }
        else if (m11 > m22)
        {
            float s = 2.0f * MathF.Sqrt(1.0f + m11 - m00 - m22);
            x = (m01 + m10) / s; y = 0.25f * s; z = (m12 + m21) / s; w = (m02 - m20) / s;
        }
        else
        {
            float s = 2.0f * MathF.Sqrt(1.0f + m22 - m00 - m11);
            x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25f * s; w = (m10 - m01) / s;
        }

        float mag = MathF.Sqrt(x * x + y * y + z * z + w * w);
        if (mag > 0.0f) { x /= mag; y /= mag; z /= mag; w /= mag; }

        // Ensure w sign matches handedness encoding used by TbnDecoder.QuatFromPacked:
        //   handedness=0 => decoder leaves w = +sqrt(...) => stored quat must have w >= 0
        //   handedness=1 => decoder negates  w = -sqrt(...) => stored quat must have w <= 0
        if (handedness == 0 && w < 0.0f) { x = -x; y = -y; z = -z; w = -w; }
        else if (handedness == 1 && w > 0.0f) { x = -x; y = -y; z = -z; w = -w; }

        ushort px = (ushort)(TbnDecoder.FloatToU15(x) | (handedness != 0 ? 0x8000 : 0));
        ushort py = (ushort)TbnDecoder.FloatToU15(y);
        ushort pz = (ushort)TbnDecoder.FloatToU15(z);
        return (px, py, pz);
    }

    static byte[] BuildTmmHeader(GlbModel model, in DataLayout dl, List<string> warnings)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((byte)0x42); w.Write((byte)0x54); w.Write((byte)0x4D); w.Write((byte)0x4D);
        w.Write(37u); // version

        // DP block (empty)
        w.Write((byte)0x44); w.Write((byte)0x50);
        w.Write(4);   // blockByteLength
        w.Write(0u);  // numImportNames

        WriteBoundingBoxes(w, model);
        w.Write(ComputeBoundsRadius(model));

        int meshGroupCount   = model.Mesh.Primitives.Length;
        int materialCount    = model.Materials.Length;
        int submodelCount    = model.Extras?.Tmm.Submodels.Length ?? 1;
        int boneCount        = model.Bones?.Length ?? 0;
        int attachmentCount  = model.Attachments?.Length ?? 0;

        int vertexCount = 0;
        int triVertCount = 0;
        foreach (var prim in model.Mesh.Primitives)
        {
            vertexCount  += prim.Positions.Length / 3;
            triVertCount += prim.Indices.Length;
        }

        w.Write((uint)meshGroupCount);
        w.Write((uint)materialCount);
        w.Write((uint)submodelCount);
        w.Write((uint)boneCount);
        w.Write(0u); // SharedAnimationBucketCount
        w.Write((uint)attachmentCount);
        w.Write((uint)vertexCount);
        w.Write((uint)triVertCount);

        // Data block layout: 7 pairs of (start, byteLength)
        // Order: vertices, indices, weights, destruction, color, heights, speedtree
        w.Write(dl.VertStart);   w.Write(dl.VertBytes);
        w.Write(dl.IdxStart);    w.Write(dl.IdxBytes);
        w.Write(0u);             w.Write(0u);  // weights (no bones)
        w.Write(0u);             w.Write(0u);  // destruction buffer
        w.Write(0u);             w.Write(0u);  // color buffer
        w.Write(dl.HeightStart); w.Write(dl.HeightBytes);
        w.Write(0u);             w.Write(0u);  // speedtree buffer

        w.Write((byte)(model.Extras?.Tmm.TerrainEmb == true ? 1 : 0));
        w.Write((byte)(model.Extras?.Tmm.Raytracing == true ? 1 : 0));

        var mm = model.Extras?.Tmm.MainMatrix ?? IdentityMatrix4x3();
        for (int i = 0; i < 12; i++) w.Write(mm[i]);

        // Attachments (zero — Tasks 13-15)
        // (none to write)

        // Mesh groups: 24 bytes each (6 x uint32)
        uint vOffset = 0, iOffset = 0;
        for (int g = 0; g < model.Mesh.Primitives.Length; g++)
        {
            var prim = model.Mesh.Primitives[g];
            uint vc = (uint)(prim.Positions.Length / 3);
            uint ic = (uint)prim.Indices.Length;

            uint matIdx = FindMaterialIndex(model.Materials, prim.MaterialName, warnings);

            w.Write(vOffset);
            w.Write(iOffset);
            w.Write(vc);
            w.Write(ic);
            w.Write(matIdx);
            w.Write(1u); // SubmodelMask: index 1 = "default" (matches Python: always 1)

            vOffset += vc;
            iOffset += ic;
        }

        // Materials
        foreach (var mat in model.Materials)
            WriteUtf16String(w, mat.Name);

        // Submodels
        var submodels = model.Extras?.Tmm.Submodels;
        if (submodels != null && submodels.Length > 0)
        {
            foreach (var s in submodels)
                WriteUtf16String(w, s);
        }
        else
        {
            WriteUtf16String(w, "default");
        }

        // Bones (zero — Tasks 13-15)
        // (none to write)

        WriteTrailingSections(w, model);

        return ms.ToArray();
    }

    static uint FindMaterialIndex(GlbMaterial[] materials, string name, List<string> warnings)
    {
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].Name == name) return (uint)i;
        }
        if (materials.Length > 0) return 0;
        warnings.Add($"Material '{name}' not found");
        return 0;
    }

    static void WriteBoundingBoxes(BinaryWriter w, GlbModel model)
    {
        var (min, max) = ComputeBbox(model);
        w.Write(min.X); w.Write(min.Y); w.Write(min.Z);
        w.Write(max.X); w.Write(max.Y); w.Write(max.Z);

        var (eMin, eMax) = ComputeExtendedBbox(model, min, max);
        w.Write(eMin.X); w.Write(eMin.Y); w.Write(eMin.Z);
        w.Write(eMax.X); w.Write(eMax.Y); w.Write(eMax.Z);
    }

    static (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) ComputeBbox(GlbModel model)
    {
        if (model.Mesh.Primitives.Length == 0)
            return (System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);

        var min = new System.Numerics.Vector3(float.PositiveInfinity);
        var max = new System.Numerics.Vector3(float.NegativeInfinity);
        foreach (var prim in model.Mesh.Primitives)
        {
            for (int i = 0; i < prim.Positions.Length; i += 3)
            {
                var p = new System.Numerics.Vector3(prim.Positions[i], prim.Positions[i + 1], prim.Positions[i + 2]);
                min = System.Numerics.Vector3.Min(min, p);
                max = System.Numerics.Vector3.Max(max, p);
            }
        }
        return (min, max);
    }

    static (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) ComputeExtendedBbox(
        GlbModel model, System.Numerics.Vector3 bboxMin, System.Numerics.Vector3 bboxMax)
    {
        if (model.Extras != null && model.Extras.Tmm.ExtendedBbox.Length == 6)
        {
            var b = model.Extras.Tmm.ExtendedBbox;
            return (new System.Numerics.Vector3(b[0], b[1], b[2]),
                    new System.Numerics.Vector3(b[3], b[4], b[5]));
        }
        bool skinned = model.Bones is { Length: > 0 };
        if (skinned) return (bboxMin * 3, bboxMax * 3);
        return (bboxMin, bboxMax);
    }

    static float ComputeBoundsRadius(GlbModel model)
    {
        var (_, max) = ComputeBbox(model);
        return max.Y;
    }

    static float[] IdentityMatrix4x3() =>
        [1, 0, 0, 0,  0, 1, 0, 0,  0, 0, 1, 0];

    static void WriteTrailingSections(BinaryWriter w, GlbModel model)
    {
        if (model.Bones is { Length: > 0 })
        {
            var modBones = model.Extras?.Tmm.ModifiedBones ?? [];
            w.Write((uint)modBones.Length);
            foreach (var mb in modBones)
            {
                w.Write(mb.BoneIndex);
                w.Write(mb.OriginalRadius);
                w.Write(mb.RadiusMultiplier);
            }
        }

        w.Write(model.Extras?.Tmm.AutoBurnMode ?? (byte)0);
        w.Write((byte)0); // hasDestruction = false
        w.Write((byte)0); // hasPhysics = false
        w.Write((byte)0); // hasTreeSkeleton = false

        w.Write((byte)0);          // clickVolumeType
        w.Write((byte)0x56); w.Write((byte)0x58); // "VX" tag
        w.Write(1);                // VX payload length
        w.Write((byte)0);          // areVoxelsDefined = false

        w.Write((byte)0); // autoAttachCorpseToBone = false
        WriteUtf16String(w, "");   // corpseBoneName
        WriteUtf16String(w, "");   // defaultDeathAnimation
        w.Write((byte)1);          // usesAutoGeneratedImpactPoints = true
        WriteUtf16String(w, "");   // defaultIdleAnimationPath
        w.Write(0u);               // manualImpactPoints count (version >= 37)
    }

    static void WriteUtf16String(BinaryWriter w, string s)
    {
        w.Write(s.Length);
        if (s.Length > 0)
            w.Write(Encoding.Unicode.GetBytes(s));
    }
}
