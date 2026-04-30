using System.Buffers.Binary;
using System.Numerics;
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

        var validate = new TmmFile(tmm);
        if (!validate.Parsed)
            throw new InvalidOperationException("TmmWriter produced output that fails parse (writer bug).");

        return (tmm, data, warnings);
    }

    // Offsets and byte lengths for each section inside the .tmm.data buffer.
    readonly struct DataLayout(
        uint vertStart, uint vertBytes,
        uint idxStart, uint idxBytes,
        uint weightStart, uint weightBytes,
        uint heightStart, uint heightBytes)
    {
        public readonly uint VertStart    = vertStart;
        public readonly uint VertBytes    = vertBytes;
        public readonly uint IdxStart     = idxStart;
        public readonly uint IdxBytes     = idxBytes;
        public readonly uint WeightStart  = weightStart;
        public readonly uint WeightBytes  = weightBytes;
        public readonly uint HeightStart  = heightStart;
        public readonly uint HeightBytes  = heightBytes;
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

        bool hasSkin = model.Bones is { Length: > 0 } &&
                       primitives.Any(p => p.JointIndices != null && p.JointWeights != null);

        uint vertBytes   = (uint)(totalVerts    * TmmVertex.SizeInBytes);
        uint idxBytes    = (uint)(totalIdxCount * 2);
        uint weightBytes = hasSkin ? (uint)(totalVerts * TmmSkinWeight.SizeInBytes) : 0u;
        uint heightBytes = (uint)(totalVerts    * 2);

        uint vertStart   = 0;
        uint idxStart    = vertStart   + vertBytes;
        uint weightStart = idxStart    + idxBytes;
        uint heightStart = weightStart + weightBytes;
        uint totalBytes  = heightStart + heightBytes;

        var buf = new byte[totalBytes];

        int vOff = (int)vertStart;
        int iOff = (int)idxStart;
        int wOff = (int)weightStart;
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

            if (hasSkin)
                WritePrimitiveSkinWeights(buf, ref wOff, prim);
        }

        layout = new DataLayout(vertStart, vertBytes, idxStart, idxBytes, weightStart, weightBytes, heightStart, heightBytes);
        return buf;
    }

    static void WritePrimitiveSkinWeights(byte[] buf, ref int wOff, GlbMeshPrimitive prim)
    {
        int vc = prim.Positions.Length / 3;
        var joints  = prim.JointIndices;
        var weights = prim.JointWeights;

        Span<(float w, byte b)> pairs  = stackalloc (float, byte)[4];
        Span<byte>              wBytes = stackalloc byte[4];

        for (int i = 0; i < vc; i++)
        {
            // Build (weight, boneIndex) pairs, keep only those with weight > 0, sort desc.
            int realCount = 0;
            for (int s = 0; s < 4; s++)
            {
                float wt = weights != null ? weights[i * 4 + s] : 0f;
                byte  bi = joints  != null ? joints [i * 4 + s] : (byte)0;
                if (wt > 0f) pairs[realCount++] = (wt, bi);
            }

            // Sort descending by weight (insertion sort on up-to-4 items).
            for (int a = 1; a < realCount; a++)
            {
                var tmp = pairs[a];
                int b = a - 1;
                while (b >= 0 && pairs[b].w < tmp.w) { pairs[b + 1] = pairs[b]; b--; }
                pairs[b + 1] = tmp;
            }

            // Quantize weights to bytes summing to 255.
            int total = 0;
            for (int s = 0; s < realCount; s++)
            {
                wBytes[s] = (byte)MathF.Round(pairs[s].w * 255f);
                total += wBytes[s];
            }
            // Adjust last real entry to absorb rounding error.
            if (realCount > 0)
                wBytes[realCount - 1] = (byte)(wBytes[realCount - 1] + (255 - total));

            // Start-pad with zeros: real entries go at indices [4 - realCount .. 3].
            int pad = 4 - realCount;
            for (int s = 0; s < 4; s++)
                buf[wOff + s] = s < pad ? (byte)0 : wBytes[s - pad];
            for (int s = 0; s < 4; s++)
                buf[wOff + 4 + s] = s < pad ? (byte)0 : pairs[s - pad].b;

            wOff += TmmSkinWeight.SizeInBytes;
        }
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

        // Game-space B = N x T. The TmmDecoder builds matrix [T | B | N] where col1 is exactly
        // this cross product (verified against vanilla samples). Using T x N would feed Shepperd
        // a left-handed frame and produce a quaternion whose decoded normal points the wrong way.
        // For mirrored faces (handedness=1) the cross still produces the "real" B; the handedness
        // bit signals the decoder to flip B back at unpack time.
        float bgx = ngy * tgz - ngz * tgy;
        float bgy = ngz * tgx - ngx * tgz;
        float bgz = ngx * tgy - ngy * tgx;

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
        w.Write(dl.VertStart);    w.Write(dl.VertBytes);
        w.Write(dl.IdxStart);     w.Write(dl.IdxBytes);
        w.Write(dl.WeightStart);  w.Write(dl.WeightBytes);
        w.Write(0u);              w.Write(0u);  // destruction buffer
        w.Write(0u);              w.Write(0u);  // color buffer
        w.Write(dl.HeightStart);  w.Write(dl.HeightBytes);
        w.Write(0u);              w.Write(0u);  // speedtree buffer

        w.Write((byte)(model.Extras?.Tmm.TerrainEmb == true ? 1 : 0));
        w.Write((byte)(model.Extras?.Tmm.Raytracing == true ? 1 : 0));

        var mm = model.Extras?.Tmm.MainMatrix ?? IdentityMatrix4x3();
        for (int i = 0; i < 12; i++) w.Write(mm[i]);

        // Attachment records
        if (model.Attachments is { Length: > 0 })
            WriteAttachments(w, model);

        // Lossy-section warnings
        foreach (var section in model.Extras?.Tmm.LossySections ?? [])
            warnings.Add($"Source had {section} data; dropped on re-import (v1 limitation)");

        // Fallback-default warnings: surface whenever the source GLB carried no
        // extras.crybar so the UI can tell the user their model is missing metadata
        // that visibly affects in-game behavior. We key off `model.Extras == null`
        // because that's the exact condition under which all extras-derived fields
        // were defaulted earlier in this method.
        if (model.Extras == null)
        {
            warnings.Add("No main_matrix in source GLB; defaulted to identity. Model may appear at wrong absolute scale in-game (relative bone motion correct).");
            if (model.Bones is { Length: > 0 })
            {
                warnings.Add("No extended_bbox in source GLB; using bbox * 3 heuristic. Animation may visibly clip near edges of unit footprint for asymmetric models.");
                warnings.Add("No auto_attach data in source GLB; defaulted to empty. Corpse-attach and impact-points behavior unavailable.");
            }
        }

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

        if (model.Bones is { Length: > 0 })
            WriteBones(w, model);

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

    static void WriteBones(BinaryWriter w, GlbModel model)
    {
        var bones = model.Bones!;

        // Build world-space matrices by walking up the parent chain.
        // LocalMatrix is column-major (glTF). Convert to System.Numerics Matrix4x4 for multiplication.
        var worldMatrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var local = MatrixDecomp.ColMajorToMatrix(bones[i].LocalMatrix);
            if (bones[i].ParentIndex < 0)
                worldMatrices[i] = local;
            else
                worldMatrices[i] = local * worldMatrices[bones[i].ParentIndex];
        }

        var collisions = model.Extras?.Tmm.BoneCollisions ?? [];
        bool hasCollisions = collisions.Length == bones.Length * 4;

        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            WriteUtf16String(w, bone.Name);
            w.Write(bone.ParentIndex);

            // Collision offset (XYZ) + radius - sourced from extras when available, else defaults.
            if (hasCollisions)
            {
                w.Write(collisions[i * 4]);
                w.Write(collisions[i * 4 + 1]);
                w.Write(collisions[i * 4 + 2]);
                w.Write(collisions[i * 4 + 3]);
            }
            else
            {
                w.Write(0f); w.Write(0f); w.Write(0f);
                w.Write(0.5f);
            }

            var parentSpaceMat = MatrixDecomp.ColMajorToMatrix(bone.LocalMatrix);
            var worldMat       = worldMatrices[i];
            var invBindMat     = MatrixDecomp.ColMajorToMatrix(bone.InverseBindMatrix);

            // Three 4x4 matrices, each with F*M*F applied (F = diag(-1,1,1,1): flip X axis)
            WriteMatrix4x4Fmf(w, parentSpaceMat);
            WriteMatrix4x4Fmf(w, worldMat);
            WriteMatrix4x4Fmf(w, invBindMat);
        }
    }

    // Applies F*M*F (F = diag(-1,1,1,1)) then writes 16 floats in TMM's flat convention.
    // F*M*F negates entries where exactly one of (row, col) is 0; entry [0,0] is double-negated.
    // Storage convention: TMM (and the python reference) stores col-major flat of the col-vector
    // matrix, which is identical to row-major flat of the equivalent System.Numerics row-vector
    // matrix (col i of M_col = row i of M_col^T = row i of M_sn). Hence we serialize System.Numerics
    // fields in row-major order: M11..M14, M21..M24, M31..M34, M41..M44.
    static void WriteMatrix4x4Fmf(BinaryWriter w, Matrix4x4 m)
    {
        var r = new Matrix4x4(
             m.M11, -m.M12, -m.M13, -m.M14,
            -m.M21,  m.M22,  m.M23,  m.M24,
            -m.M31,  m.M32,  m.M33,  m.M34,
            -m.M41,  m.M42,  m.M43,  m.M44);

        w.Write(r.M11); w.Write(r.M12); w.Write(r.M13); w.Write(r.M14);
        w.Write(r.M21); w.Write(r.M22); w.Write(r.M23); w.Write(r.M24);
        w.Write(r.M31); w.Write(r.M32); w.Write(r.M33); w.Write(r.M34);
        w.Write(r.M41); w.Write(r.M42); w.Write(r.M43); w.Write(r.M44);
    }

    static void WriteAttachments(BinaryWriter w, GlbModel model)
    {
        var attachments = model.Attachments!;
        var extrasAttachments = model.Extras?.Tmm.Attachments ?? [];

        for (int i = 0; i < attachments.Length; i++)
        {
            var att = attachments[i];

            // Find matching extras entry by name (case-sensitive, exact match)
            GlbExtras.AttachmentEntry? extras = null;
            foreach (var e in extrasAttachments)
            {
                if (e.Name == att.Name) { extras = e; break; }
            }

            w.Write(extras?.TypeFlag ?? 0u);
            w.Write(att.ParentBoneIndex);
            WriteUtf16String(w, att.Name);

            // Slot 1 (AdjustmentTransformMatrix) is reconstructed from the glTF node matrix
            // (which GlbExporter sourced from the original AdjustmentTransformMatrix). The glTF
            // flat is col-major-4x4 of M_col with F*M*F (axis-flip indices {1,2,3,4,8,12}) applied;
            // we undo the flip and extract rows 0/1/2 to produce TMM's row-major-3x4 layout.
            WriteAdjustmentMatrixFromGlb(w, att.LocalMatrix);

            // Slot 2 (LocalTransformMatrix) is preserved verbatim through GlbExtras as 12 floats
            // in TMM's native row-major-3x4 layout; pass through untransformed.
            var localMat = extras?.LocalMatrix ?? IdentityMatrix4x3();
            for (int j = 0; j < 12; j++) w.Write(j < localMat.Length ? localMat[j] : 0f);

            w.Write(extras?.DummyBoneMode ?? 0u);
            w.Write(extras?.DummyBoneTransformMode ?? 0u);
            WriteUtf16String(w, extras?.ForcedDummyBoneName ?? "");
            w.Write(extras?.FrameLimit ?? -1);
            w.Write(extras?.FramePosition ?? 0f);
            w.Write(extras?.DummyBoneAnimationFilter ?? 0u);

            var anims = extras?.SpecificAnimations ?? [];
            w.Write((uint)anims.Length);
            foreach (var anim in anims) WriteUtf16String(w, anim);
        }
    }

    // Inverts the per-flat-index axis flip applied by GlbExporter on attachment node matrices,
    // then writes the reconstructed TMM row-major-3x4 layout (rows 0/1/2 of M_col).
    // Source flat is col-major-4x4 of (F*M_col*F): col-major flat[col*4 + row], so row r of M_col
    // is at flat indices (0*4+r), (1*4+r), (2*4+r), (3*4+r) = r, r+4, r+8, r+12.
    static void WriteAdjustmentMatrixFromGlb(BinaryWriter w, float[] flat)
    {
        Span<float> u = stackalloc float[16];
        flat.CopyTo(u);
        u[1] = -u[1]; u[2] = -u[2]; u[3] = -u[3];
        u[4] = -u[4]; u[8] = -u[8]; u[12] = -u[12];

        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 4; col++)
                w.Write(u[col * 4 + row]);
    }

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

        var aa = model.Extras?.Tmm.AutoAttach;
        w.Write((byte)(aa?.AutoAttachCorpseToBone == true ? 1 : 0));
        WriteUtf16String(w, aa?.CorpseAttachBoneName ?? "");
        WriteUtf16String(w, aa?.DefaultDeathAnimation ?? "");
        w.Write((byte)(aa == null || aa.UsesAutoGeneratedImpactPoints ? 1 : 0));
        WriteUtf16String(w, aa?.DefaultIdleAnimationPath ?? "");

        var impactPoints = model.Extras?.Tmm.ImpactPoints ?? [];
        w.Write((uint)impactPoints.Length);
        foreach (var pt in impactPoints)
            for (int i = 0; i < 4; i++) w.Write(i < pt.Length ? pt[i] : 0f);
    }

    static void WriteUtf16String(BinaryWriter w, string s)
    {
        w.Write(s.Length);
        if (s.Length > 0)
            w.Write(Encoding.Unicode.GetBytes(s));
    }
}
