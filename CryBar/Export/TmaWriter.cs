using System.Numerics;
using System.Text;
using CryBar.TMM;

namespace CryBar.Export;

public static class TmaWriter
{
    public static (byte[] Tma, IReadOnlyList<string> Warnings) Write(
        GlbAnimation anim, GlbBone[] bones, GlbExtras.TmaSection? extras)
    {
        var warnings = new List<string>();
        var allControllers = extras?.Controllers ?? [];
        var controllers = allControllers
            .Where(c => c.Type == TmaControllerType.Visibility || c.Type == TmaControllerType.Footprint)
            .ToArray();
        foreach (var skipped in allControllers.Where(c => c.Type != TmaControllerType.Visibility && c.Type != TmaControllerType.Footprint))
            warnings.Add($"Animation '{anim.Name}' had unknown controller type {skipped.Type}; skipped.");

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // BTMA magic
        w.Write((byte)0x42); w.Write((byte)0x54); w.Write((byte)0x4D); w.Write((byte)0x41);
        w.Write(12u); // version

        // DP block (empty)
        w.Write((byte)0x44); w.Write((byte)0x50);
        w.Write(4);  // blockByteLength
        w.Write(0u); // numImportNames

        w.Write((uint)bones.Length); // numTracks
        w.Write(anim.FrameCount);
        w.Write(anim.Duration);

        // Root bbox: 2 x 3 floats (min XYZ, max XYZ) - zeroed when no positional data
        for (int i = 0; i < 6; i++) w.Write(0f);

        w.Write((uint)bones.Length); // numBones
        w.Write((uint)controllers.Length);

        WriteBones(w, bones);
        WriteTracks(w, anim, bones, warnings);
        WriteControllers(w, controllers, anim.Name, warnings);

        // Error section
        w.Write(0u); // errorFlags
        w.Write(0u); // errorCount

        var bytes = ms.ToArray();

        var validate = new TmaFile(bytes);
        if (!validate.Parsed)
            throw new InvalidOperationException("TmaWriter produced output that fails parse (writer bug).");

        return (bytes, warnings);
    }

    static void WriteBones(BinaryWriter w, GlbBone[] bones)
    {
        var worldMatrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var local = MatrixDecomp.ColMajorToMatrix(bones[i].LocalMatrix);
            worldMatrices[i] = bones[i].ParentIndex < 0
                ? local
                : local * worldMatrices[bones[i].ParentIndex];
        }

        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            WriteUtf16String(w, bone.Name);
            w.Write(bone.ParentIndex);

            // localTransform = parent-relative (local) matrix
            WriteMatrix4x4Fmf(w, MatrixDecomp.ColMajorToMatrix(bone.LocalMatrix));
            // bindPose = world-space matrix
            WriteMatrix4x4Fmf(w, worldMatrices[i]);
            // inverseBindPose
            WriteMatrix4x4Fmf(w, MatrixDecomp.ColMajorToMatrix(bone.InverseBindMatrix));
        }
    }

    static void WriteTracks(BinaryWriter w, GlbAnimation anim, GlbBone[] bones, List<string> warnings)
    {
        int frameCount = (int)anim.FrameCount;

        for (int i = 0; i < bones.Length; i++)
        {
            WriteUtf16String(w, bones[i].Name);

            // trackVersion=1, translationEncoding=Raw(1), rotationEncoding=Quat64(3), scaleEncoding=Constant(0)
            w.Write((byte)1);
            w.Write((byte)1); // Raw translation
            w.Write((byte)3); // Quat64 rotation
            w.Write((byte)0); // Constant scale

            w.Write(frameCount);

            // bones[i].LocalMatrix is in glTF (axis-mirrored) space, so the decomposed bind pose
            // is mirror(bindT_orig) and mirror_quat(bindR_orig). The forward composition in
            // GlbExporter uses bindT_orig / bindR_orig (the original game-space pose), so we must
            // unmirror back before subtracting/inverting to recover the original delta.
            MatrixDecomp.Decompose(bones[i].LocalMatrix, out var bindT_glb, out var bindR_glb, out _);
            var bindT = new Vector3(-bindT_glb.X, bindT_glb.Y, bindT_glb.Z);
            var bindR = new Quaternion(bindR_glb.X, -bindR_glb.Y, -bindR_glb.Z, bindR_glb.W);
            var invBindR = Quaternion.Inverse(bindR);

            GlbBoneTrack? track = null;
            if (anim.Tracks != null)
            {
                foreach (var t in anim.Tracks)
                {
                    if (t.BoneIndex == i) { track = t; break; }
                }
            }

            // Translation: Raw = 4-byte size prefix + frameCount * 12 bytes
            int translationBytes = frameCount * 12;
            w.Write(translationBytes);
            for (int f = 0; f < frameCount; f++)
            {
                var glbT = SampleTrackTranslation(track, f, frameCount, anim.Duration, bindT);
                // Forward: glbT = mirror(bindT + tmaT). Reverse: tmaT = unmirror(glbT) - bindT.
                var tmaT = new Vector3(-glbT.X, glbT.Y, glbT.Z) - bindT;
                w.Write(tmaT.X);
                w.Write(tmaT.Y);
                w.Write(tmaT.Z);
            }

            // Rotation: Quat64 = 4-byte size prefix + frameCount * 8 bytes
            int rotationBytes = frameCount * 8;
            w.Write(rotationBytes);
            for (int f = 0; f < frameCount; f++)
            {
                var glbR = SampleTrackRotation(track, f, frameCount, anim.Duration, bindR);
                // Forward: glbR = mirror(bindR * conj(tmaR)). Reverse: tmaR = conj(invBindR * unmirror(glbR)).
                var unmirrored = new Quaternion(glbR.X, -glbR.Y, -glbR.Z, glbR.W);
                var tmaR = Quaternion.Conjugate(invBindR * unmirrored);
                w.Write(EncodeQuat64(tmaR));
            }

            // Scale: Constant = 16 bytes inline (__m128), uniform scale 1,1,1
            w.Write(1f); w.Write(1f); w.Write(1f); w.Write(0f); // XYZ scale + padding
        }
    }

    static void WriteControllers(BinaryWriter w, GlbExtras.TmaControllerEntry[] controllers, string animName, List<string> warnings)
    {
        foreach (var c in controllers)
        {
            switch (c.Type)
            {
                case TmaControllerType.Visibility:
                    w.Write(TmaControllerType.Visibility);
                    w.Write(c.Start);
                    w.Write(c.End);
                    w.Write(c.EaseIn);
                    w.Write(c.EaseOut);
                    w.Write((byte)(c.InvertLogic ? 1 : 0));
                    WriteUtf16String(w, c.AttachPointName);
                    break;
                case TmaControllerType.Footprint:
                    w.Write(TmaControllerType.Footprint);
                    w.Write(c.SpawnTime);
                    WriteUtf16String(w, c.FootprintName);
                    w.Write(c.FootprintId);
                    w.Write((byte)(c.InvertTextureY ? 1 : 0));
                    WriteUtf16String(w, c.AttachPointName);
                    w.Write((byte)(c.IsRightSide ? 1 : 0));
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled controller type {c.Type} (should have been filtered).");
            }
        }
    }

    // Returns the glTF-space translation for frame f.
    // If no track or empty track: rest pose = mirror(bindT) = (-bindT.X, bindT.Y, bindT.Z).
    // If sample count matches frameCount: direct lookup.
    // Otherwise: LERP resample at uniform time t = f * duration / (frameCount - 1).
    static Vector3 SampleTrackTranslation(GlbBoneTrack? track, int f, int frameCount, float duration, Vector3 bindT)
    {
        if (track == null || track.Translations.Length == 0)
            return new Vector3(-bindT.X, bindT.Y, bindT.Z);

        var samples = track.Translations;
        if (samples.Length == frameCount)
            return samples[f];

        float t = frameCount > 1 ? f * duration / (frameCount - 1) : 0f;
        return LerpVec3Uniform(samples, t, duration);
    }

    // Returns the glTF-space rotation for frame f.
    // If no track or empty track: rest pose = mirror(bindR) = (bindR.X, -bindR.Y, -bindR.Z, bindR.W).
    // If sample count matches frameCount: direct lookup.
    // Otherwise: SLERP resample at uniform time t = f * duration / (frameCount - 1).
    static Quaternion SampleTrackRotation(GlbBoneTrack? track, int f, int frameCount, float duration, Quaternion bindR)
    {
        if (track == null || track.Rotations.Length == 0)
            return new Quaternion(bindR.X, -bindR.Y, -bindR.Z, bindR.W);

        var samples = track.Rotations;
        if (samples.Length == frameCount)
            return samples[f];

        float t = frameCount > 1 ? f * duration / (frameCount - 1) : 0f;
        return SlerpQuatUniform(samples, t, duration);
    }

    // LERP on a uniformly-sampled Vector3 array where sample i is at time i*duration/(n-1).
    static Vector3 LerpVec3Uniform(Vector3[] samples, float t, float duration)
    {
        int n = samples.Length;
        if (n == 1 || duration <= 0f) return samples[0];
        float tNorm = Math.Clamp(t / duration, 0f, 1f) * (n - 1);
        int lo = (int)tNorm;
        int hi = Math.Min(lo + 1, n - 1);
        float a = tNorm - lo;
        return Vector3.Lerp(samples[lo], samples[hi], a);
    }

    // SLERP on a uniformly-sampled Quaternion array where sample i is at time i*duration/(n-1).
    static Quaternion SlerpQuatUniform(Quaternion[] samples, float t, float duration)
    {
        int n = samples.Length;
        if (n == 1 || duration <= 0f) return samples[0];
        float tNorm = Math.Clamp(t / duration, 0f, 1f) * (n - 1);
        int lo = (int)tNorm;
        int hi = Math.Min(lo + 1, n - 1);
        float a = tNorm - lo;
        return Quaternion.Slerp(samples[lo], samples[hi], a);
    }

    // Encodes a quaternion into Quat64 "smallest three" format (8 bytes).
    // Layout: [4-bit dropped-index][20-bit C2][20-bit C1][20-bit C0], each 20-bit = sign + 19-bit magnitude.
    internal static ulong EncodeQuat64(Quaternion q)
    {
        // Normalize
        float mag = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        if (mag > 0f) { q = new Quaternion(q.X / mag, q.Y / mag, q.Z / mag, q.W / mag); }

        float[] c = [q.X, q.Y, q.Z, q.W];

        // Find the largest absolute component
        int largestIdx = 0;
        float largestAbs = Math.Abs(c[0]);
        for (int i = 1; i < 4; i++)
        {
            float a = Math.Abs(c[i]);
            if (a > largestAbs) { largestAbs = a; largestIdx = i; }
        }

        // Ensure largest component is positive (canonical form)
        if (c[largestIdx] < 0f)
            for (int i = 0; i < 4; i++) c[i] = -c[i];

        const float Scale = 0.70710678118f; // 1/sqrt(2): max magnitude of a non-largest component
        const float MaxMag = 524287f;        // 2^19 - 1

        // Collect the three non-largest components.
        // Decoder reads comp=3 from low bits, comp=2 from next, comp=1 from highest,
        // so we pack in descending component order (3->2->1->0 skipping largestIdx).
        ulong packed = 0;
        int bitOffset = 0;
        for (int i = 3; i >= 0; i--)
        {
            if (i == largestIdx) continue;
            float val = c[i] / Scale;
            float clamped = Math.Clamp(val, -1f, 1f);
            uint magnitude = (uint)MathF.Round(MathF.Abs(clamped) * MaxMag);
            uint signBit = clamped < 0f ? 1u : 0u; // decoder: negative = (bit >> 19) != 0
            // 19-bit magnitude, then 1 sign bit
            packed |= ((ulong)(magnitude & 0x7FFFF)) << bitOffset;
            bitOffset += 19;
            packed |= ((ulong)signBit) << bitOffset;
            bitOffset++;
        }

        // Dropped-component index occupies bits [63:60] (top 4 bits).
        // Decoder uses (packed >> 60) & 0xF as index.
        // Map from component order [X=0,Y=1,Z=2,W=3] to the index the decoder expects.
        // TmaDecoder reads: idx = (packed >> 60) & 0xF, then fills slots 3,2,1,0 skipping idx.
        // We need largestIdx to map such that the decoder reconstructs the dropped component correctly.
        // The decoder slot order (high to low): slot3=comp3, slot2=comp2, slot1=comp1, slot0=comp0,
        // where comp 0,1,2,3 map to X,Y,Z,W. So idx directly equals largestIdx.
        packed |= ((ulong)(largestIdx & 0xF)) << 60;

        return packed;
    }

    // Applies F*M*F (F = diag(-1,1,1,1)) then writes 16 floats in TMA's flat convention.
    // Storage convention is identical to TMM: col-major flat of the col-vector matrix, which
    // is row-major flat of the equivalent System.Numerics row-vector matrix. Verified by
    // byte-comparing vanilla TMM and TMA bones describing the same skeleton.
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

    static void WriteUtf16String(BinaryWriter w, string s)
    {
        w.Write(s.Length);
        if (s.Length > 0)
            w.Write(Encoding.Unicode.GetBytes(s));
    }
}
