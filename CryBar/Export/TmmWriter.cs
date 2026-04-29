using System.Text;
using CryBar.TMM;

namespace CryBar.Export;

public static class TmmWriter
{
    public static (byte[] Tmm, byte[] TmmData, IReadOnlyList<string> Warnings) Write(GlbModel model)
    {
        var warnings = new List<string>();
        using var tmmMs = new MemoryStream();
        using var w = new BinaryWriter(tmmMs);

        // Magic: "BTMM"
        w.Write((byte)0x42); w.Write((byte)0x54); w.Write((byte)0x4D); w.Write((byte)0x4D);
        w.Write(37u);

        // DP block (empty)
        w.Write((byte)0x44); w.Write((byte)0x50);
        w.Write(4);  // blockByteLength covers the numImportNames uint32 below
        w.Write(0u); // numImportNames

        WriteBoundingBoxes(w, model);

        // Bounds radius -- heuristic (bbox.maxY) when extras absent, else from extras
        w.Write(ComputeBoundsRadius(model));

        int meshGroupCount = model.Mesh.Primitives.Length;
        int materialCount = model.Materials.Length;
        int submodelCount = model.Extras?.Tmm.Submodels.Length ?? 1;
        int boneCount = model.Bones?.Length ?? 0;
        int attachmentCount = model.Attachments?.Length ?? 0;
        int vertexCount = 0;
        int triangleVertCount = 0;
        foreach (var prim in model.Mesh.Primitives)
        {
            vertexCount += prim.Positions.Length / 3;
            triangleVertCount += prim.Indices.Length;
        }

        w.Write((uint)meshGroupCount);
        w.Write((uint)materialCount);
        w.Write((uint)submodelCount);
        w.Write((uint)boneCount);
        w.Write(0u); // SharedAnimationBucketCount
        w.Write((uint)attachmentCount);
        w.Write((uint)vertexCount);
        w.Write((uint)triangleVertCount);

        // Data block layout: 7 pairs (start, byteLength) = 14 uint32s
        for (int i = 0; i < 14; i++) w.Write(0u);

        w.Write((byte)(model.Extras?.Tmm.TerrainEmb == true ? 1 : 0));
        w.Write((byte)(model.Extras?.Tmm.Raytracing == true ? 1 : 0));

        // Main matrix stored as 4x3 (12 floats); reader expands to 4x4 by appending identity row
        var mm = model.Extras?.Tmm.MainMatrix ?? IdentityMatrix4x3();
        for (int i = 0; i < 12; i++) w.Write(mm[i]);

        WriteTrailingSections(w, model);

        return (tmmMs.ToArray(), [], warnings);
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
        // Skinned heuristic: 3x scale; static: same as bbox.
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
        // ModifiedBones — only written when NumBones > 0
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

        w.Write(model.Extras?.Tmm.AutoBurnMode ?? (byte)0); // AutoBurnMode (raw byte)
        w.Write((byte)0); // hasDestruction = false
        w.Write((byte)0); // hasPhysics = false
        w.Write((byte)0); // hasTreeSkeleton = false

        // Click volume: 1-byte type, then VX TLV block
        w.Write((byte)0); // clickVolumeType
        w.Write((byte)0x56); w.Write((byte)0x58); // "VX" tag
        w.Write(1);        // VX payload length = 1 byte
        w.Write((byte)0);  // areVoxelsDefined = false

        // Auto-attach (version >= 36)
        w.Write((byte)0); // autoAttachCorpseToBone = false
        WriteUtf16String(w, ""); // corpseBoneName
        WriteUtf16String(w, ""); // defaultDeathAnimation
        w.Write((byte)1); // usesAutoGeneratedImpactPoints = true

        WriteUtf16String(w, ""); // defaultIdleAnimationPath

        // ManualImpactPoints (version >= 37)
        w.Write(0u);
    }

    static void WriteUtf16String(BinaryWriter w, string s)
    {
        // char count (int32) followed by UTF-16LE bytes — mirrors TryReadUTF16String
        w.Write(s.Length);
        if (s.Length > 0)
            w.Write(Encoding.Unicode.GetBytes(s));
    }
}
