using System.Buffers.Binary;
using System.Text.Json;

namespace CryBar.Export;

public sealed class GlbModel
{
    public required GlbMesh Mesh { get; init; }
    public GlbBone[]? Bones { get; init; }
    public GlbAttachment[]? Attachments { get; init; }
    public GlbAnimation[]? Animations { get; init; }
    public GlbMaterial[] Materials { get; init; } = [];
    public GlbExtras? Extras { get; init; }
}

public sealed class GlbMesh
{
    public GlbMeshPrimitive[] Primitives { get; init; } = [];
}

public sealed class GlbMeshPrimitive
{
    public required string MaterialName { get; init; }
    public required float[] Positions { get; init; }   // length = vertexCount * 3
    public required float[] Normals { get; init; }     // length = vertexCount * 3
    public required float[] Tangents { get; init; }    // length = vertexCount * 4 (xyz + w sign)
    public required float[] TexCoords { get; init; }   // length = vertexCount * 2
    public byte[]? JointIndices { get; init; }         // length = vertexCount * 4 (u8) or null
    public float[]? JointWeights { get; init; }        // length = vertexCount * 4 or null
    public required uint[] Indices { get; init; }
}

public sealed class GlbBone
{
    public required string Name { get; init; }
    public int ParentIndex { get; init; } = -1;
    public required float[] LocalMatrix { get; init; }       // 16 floats column-major
    public required float[] InverseBindMatrix { get; init; } // 16 floats column-major
}

public sealed class GlbAttachment
{
    public required string Name { get; init; }
    public int ParentBoneIndex { get; init; } = -1;
    public required float[] LocalMatrix { get; init; }  // 16 floats column-major (glTF node transform)
    public required int Index { get; init; }             // from extras.crybar.node_type marker
}

public sealed class GlbAnimation
{
    public required string Name { get; init; }
    public required GlbBoneTrack[] Tracks { get; init; } // one per bone, may be all-null if static
    public float Duration { get; init; }
    public uint FrameCount { get; init; }                // resampled count (from extras or inferred)
}

public sealed class GlbBoneTrack
{
    public required int BoneIndex { get; init; }
    public required System.Numerics.Vector3[] Translations { get; init; } // length = frameCount
    public required System.Numerics.Quaternion[] Rotations { get; init; } // length = frameCount
}

public sealed class GlbMaterial
{
    public required string Name { get; init; }
    public byte[]? BaseColorPng { get; init; }
    public byte[]? NormalMapPng { get; init; }
}

public static class GlbReader
{
    const uint GlbMagic = 0x46546C67;      // "glTF"
    const uint ChunkTypeJson = 0x4E4F534A; // "JSON"
    const uint ChunkTypeBin = 0x004E4942;  // "BIN\0"

    static readonly System.Text.RegularExpressions.Regex SuffixRegex =
        new(@"\.\d{3}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static string StripSuffixForTests(string s) => SuffixRegex.Replace(s, "");

    /// <summary>Parses a GLB byte stream into an in-memory <see cref="GlbModel"/>.</summary>
    public static GlbModel Parse(ReadOnlyMemory<byte> glb)
    {
        var (json, bin) = ParseContainer(glb);
        using (json)
        {
            var root = json.RootElement;

            GlbExtras? extras = null;
            if (root.TryGetProperty("extras", out var extrasEl))
                extras = GlbExtras.Read(extrasEl);
            extras = MergeMeshNodeMainMatrix(extras, root);

            var mesh = ReadMesh(root, bin);
            var bones = ReadBones(root, bin);
            var sceneRoot = ComputeSceneRootTransform(root);
            mesh = BakeRootTransformIntoMesh(mesh, sceneRoot);
            bones = BakeRootTransformIntoBones(bones, sceneRoot);
            var animations = ReadAnimations(root, bin, bones, extras);
            var materials = ReadMaterials(root, bin);
            var attachments = ReadAttachments(root, extras, bones);

            return new GlbModel { Mesh = mesh, Bones = bones, Animations = animations, Attachments = attachments, Materials = materials, Extras = extras };
        }
    }

    static GlbExtras? MergeMeshNodeMainMatrix(GlbExtras? assetExtras, JsonElement root)
    {
        if (!root.TryGetProperty("nodes", out var nodes)) return assetExtras;
        foreach (var node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("mesh", out _)) continue;
            if (!node.TryGetProperty("extras", out var nx)) continue;
            if (!nx.TryGetProperty("crybar", out var cb)) continue;
            if (!cb.TryGetProperty("main_matrix", out var mm)) continue;

            // Mesh-node main_matrix wins.
            var result = assetExtras ?? new GlbExtras();
            var arr = new float[mm.GetArrayLength()];
            for (int i = 0; i < arr.Length; i++) arr[i] = mm[i].GetSingle();
            result.Tmm.MainMatrix = arr;
            return result;
        }
        return assetExtras;
    }

    static GlbAttachment[]? ReadAttachments(JsonElement root, GlbExtras? extras, GlbBone[]? bones)
    {
        if (!root.TryGetProperty("nodes", out var nodes)) return null;

        // Build node-to-bone lookup so we can find an attachment's parent bone.
        var nodeToBone = new Dictionary<int, int>();
        if (bones != null && root.TryGetProperty("skins", out var skins) && skins.GetArrayLength() > 0)
        {
            var joints = skins[0].GetProperty("joints");
            for (int i = 0; i < joints.GetArrayLength(); i++) nodeToBone[joints[i].GetInt32()] = i;
        }

        // Build child-to-parent map for non-joint nodes.
        var childToParent = new Dictionary<int, int>();
        for (int n = 0; n < nodes.GetArrayLength(); n++)
        {
            if (!nodes[n].TryGetProperty("children", out var children)) continue;
            foreach (var c in children.EnumerateArray())
                childToParent[c.GetInt32()] = n;
        }

        var attachments = new List<GlbAttachment>();
        for (int n = 0; n < nodes.GetArrayLength(); n++)
        {
            var node = nodes[n];
            if (!node.TryGetProperty("extras", out var nx)) continue;
            if (!nx.TryGetProperty("crybar", out var cb)) continue;
            if (!cb.TryGetProperty("node_type", out var nt) || nt.GetString() != "attachment") continue;

            int attIdx = cb.GetProperty("index").GetInt32();
            string name = SuffixRegex.Replace(
                node.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "", "");
            float[] local = ReadNodeTransform(node);

            int parentBone = -1;
            if (childToParent.TryGetValue(n, out int parentNode))
                nodeToBone.TryGetValue(parentNode, out parentBone);

            attachments.Add(new GlbAttachment
            {
                Name = name,
                Index = attIdx,
                ParentBoneIndex = parentBone,
                LocalMatrix = local,
            });
        }
        if (attachments.Count == 0) return null;

        attachments.Sort((a, b) => a.Index.CompareTo(b.Index));
        return attachments.ToArray();
    }

    internal static (JsonDocument Json, byte[] Bin) ParseContainerForTests(ReadOnlyMemory<byte> glb)
        => ParseContainer(glb);

    static GlbMesh ReadMesh(JsonElement root, byte[] bin)
    {
        if (!root.TryGetProperty("meshes", out var meshes) || meshes.GetArrayLength() == 0)
            throw new GlbParseException("GLB has no meshes.");
        if (meshes.GetArrayLength() > 1)
            throw new GlbParseException(
                $"Multi-mesh GLBs are not supported (found {meshes.GetArrayLength()}). Merge meshes in Blender first.");

        var mesh = meshes[0];
        if (!mesh.TryGetProperty("primitives", out var primsEl))
            throw new GlbParseException("Mesh has no primitives.");

        var prims = new List<GlbMeshPrimitive>();
        string[] materialNames = ReadMaterialNames(root);

        foreach (var p in primsEl.EnumerateArray())
        {
            var attrs = p.GetProperty("attributes");
            int posAcc = attrs.GetProperty("POSITION").GetInt32();
            int normAcc = attrs.GetProperty("NORMAL").GetInt32();
            int tanAcc = attrs.GetProperty("TANGENT").GetInt32();
            int uvAcc = attrs.GetProperty("TEXCOORD_0").GetInt32();
            int indicesAcc = p.GetProperty("indices").GetInt32();

            byte[]? joints = null;
            float[]? weights = null;
            if (attrs.TryGetProperty("JOINTS_0", out var j))
                joints = ReadAccessorBytes(root, bin, j.GetInt32());
            if (attrs.TryGetProperty("WEIGHTS_0", out var w))
                weights = ReadAccessorFloats(root, bin, w.GetInt32());

            string materialName = "default";
            if (p.TryGetProperty("material", out var matIdx))
                materialName = materialNames[matIdx.GetInt32()];

            prims.Add(new GlbMeshPrimitive
            {
                MaterialName = materialName,
                Positions = ReadAccessorFloats(root, bin, posAcc),
                Normals = ReadAccessorFloats(root, bin, normAcc),
                Tangents = ReadAccessorFloats(root, bin, tanAcc),
                TexCoords = ReadAccessorFloats(root, bin, uvAcc),
                JointIndices = joints,
                JointWeights = weights,
                Indices = ReadAccessorIndices(root, bin, indicesAcc),
            });
        }

        return new GlbMesh { Primitives = prims.ToArray() };
    }

    static string[] ReadMaterialNames(JsonElement root)
    {
        if (!root.TryGetProperty("materials", out var mats)) return [];
        var names = new List<string>();
        foreach (var m in mats.EnumerateArray())
            names.Add(SuffixRegex.Replace(m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", ""));
        return names.ToArray();
    }

    static GlbBone[]? ReadBones(JsonElement root, byte[] bin)
    {
        if (!root.TryGetProperty("skins", out var skins) || skins.GetArrayLength() == 0)
            return null;

        var skin = skins[0];
        var joints = skin.GetProperty("joints");
        int boneCount = joints.GetArrayLength();
        int[] jointNodeIndices = new int[boneCount];
        for (int i = 0; i < boneCount; i++) jointNodeIndices[i] = joints[i].GetInt32();

        var nodeToBone = new Dictionary<int, int>();
        for (int i = 0; i < boneCount; i++) nodeToBone[jointNodeIndices[i]] = i;

        var nodes = root.GetProperty("nodes");
        int[] parents = new int[boneCount];
        for (int i = 0; i < boneCount; i++) parents[i] = -1;
        for (int n = 0; n < nodes.GetArrayLength(); n++)
        {
            if (!nodes[n].TryGetProperty("children", out var children)) continue;
            if (!nodeToBone.TryGetValue(n, out int parentBoneIdx)) continue;
            foreach (var c in children.EnumerateArray())
            {
                int childNode = c.GetInt32();
                if (nodeToBone.TryGetValue(childNode, out int childBoneIdx))
                    parents[childBoneIdx] = parentBoneIdx;
            }
        }

        float[]? ibmFlat = null;
        if (skin.TryGetProperty("inverseBindMatrices", out var ibmIdx))
            ibmFlat = ReadAccessorFloats(root, bin, ibmIdx.GetInt32());

        var bones = new GlbBone[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            var node = nodes[jointNodeIndices[i]];
            string name = SuffixRegex.Replace(
                node.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "", "");
            float[] local = ReadNodeTransform(node);
            float[] ibm = ibmFlat != null
                ? ibmFlat.AsSpan(i * 16, 16).ToArray()
                : Identity4x4();

            bones[i] = new GlbBone
            {
                Name = name,
                ParentIndex = parents[i],
                LocalMatrix = local,
                InverseBindMatrix = ibm,
            };
        }
        return bones;
    }

    static float[] ReadNodeTransform(JsonElement node)
    {
        // glTF: either "matrix" (16 floats column-major) or "translation"+"rotation"+"scale".
        if (node.TryGetProperty("matrix", out var mEl))
        {
            var m = new float[16];
            int i = 0;
            foreach (var v in mEl.EnumerateArray()) m[i++] = v.GetSingle();
            return m;
        }
        var t = ReadVec3(node, "translation", System.Numerics.Vector3.Zero);
        var r = ReadQuat(node, "rotation", System.Numerics.Quaternion.Identity);
        var s = ReadVec3(node, "scale", System.Numerics.Vector3.One);

        var matT = System.Numerics.Matrix4x4.CreateTranslation(t);
        var matR = System.Numerics.Matrix4x4.CreateFromQuaternion(r);
        var matS = System.Numerics.Matrix4x4.CreateScale(s);
        var mat = matS * matR * matT; // glTF: M = T * R * S applied to a vector v as v' = T(R(S v))

        return [
            mat.M11, mat.M12, mat.M13, mat.M14,
            mat.M21, mat.M22, mat.M23, mat.M24,
            mat.M31, mat.M32, mat.M33, mat.M34,
            mat.M41, mat.M42, mat.M43, mat.M44,
        ];
    }

    static System.Numerics.Vector3 ReadVec3(JsonElement node, string key, System.Numerics.Vector3 def)
    {
        if (!node.TryGetProperty(key, out var el)) return def;
        return new System.Numerics.Vector3(el[0].GetSingle(), el[1].GetSingle(), el[2].GetSingle());
    }

    static System.Numerics.Quaternion ReadQuat(JsonElement node, string key, System.Numerics.Quaternion def)
    {
        if (!node.TryGetProperty(key, out var el)) return def;
        return new System.Numerics.Quaternion(
            el[0].GetSingle(), el[1].GetSingle(), el[2].GetSingle(), el[3].GetSingle());
    }

    static float[] Identity4x4() =>
        [1, 0, 0, 0,  0, 1, 0, 0,  0, 0, 1, 0,  0, 0, 0, 1];

    static float[] ReadAccessorFloats(JsonElement root, byte[] bin, int accessorIdx)
    {
        var (offset, count, _, components) =
            ResolveAccessor(root, accessorIdx, expectFloat: true, bin.Length);
        var result = new float[count * components];
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
            bin.AsSpan(offset, count * components * 4)).CopyTo(result);
        return result;
    }

    static byte[] ReadAccessorBytes(JsonElement root, byte[] bin, int accessorIdx)
    {
        var (offset, count, componentSize, components) =
            ResolveAccessor(root, accessorIdx, expectFloat: false, bin.Length);
        if (componentSize != 1)
            throw new GlbParseException("Expected u8 joint indices.");
        return bin.AsSpan(offset, count * components).ToArray();
    }

    static uint[] ReadAccessorIndices(JsonElement root, byte[] bin, int accessorIdx)
    {
        var (offset, count, componentSize, _) =
            ResolveAccessor(root, accessorIdx, expectFloat: false, bin.Length);
        var result = new uint[count];
        if (componentSize == 2)
        {
            var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(
                bin.AsSpan(offset, count * 2));
            for (int i = 0; i < count; i++) result[i] = src[i];
        }
        else if (componentSize == 4)
        {
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(
                bin.AsSpan(offset, count * 4)).CopyTo(result);
        }
        else throw new GlbParseException($"Unsupported index component size {componentSize}.");
        return result;
    }

    static (int Offset, int Count, int ComponentSize, int Components) ResolveAccessor(
        JsonElement root, int accessorIdx, bool expectFloat, int binLength)
    {
        var accessors = root.GetProperty("accessors");
        var acc = accessors[accessorIdx];

        if (acc.TryGetProperty("sparse", out _))
            throw new GlbParseException("Sparse accessors are not supported.");

        int count = acc.GetProperty("count").GetInt32();
        int componentType = acc.GetProperty("componentType").GetInt32();
        string type = acc.GetProperty("type").GetString() ?? "";
        int components = type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2, "VEC3" => 3, "VEC4" => 4,
            "MAT4" => 16,
            _ => throw new GlbParseException($"Unsupported accessor type {type}.")
        };
        int componentSize = componentType switch
        {
            5121 => 1, // u8
            5123 => 2, // u16
            5125 => 4, // u32
            5126 => 4, // f32
            _ => throw new GlbParseException($"Unsupported componentType {componentType}.")
        };
        if (expectFloat && componentType != 5126)
            throw new GlbParseException($"Expected float accessor, got componentType {componentType}.");

        int bufferViewIdx = acc.GetProperty("bufferView").GetInt32();
        var bv = root.GetProperty("bufferViews")[bufferViewIdx];
        int bvOffset = bv.TryGetProperty("byteOffset", out var bvo) ? bvo.GetInt32() : 0;
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        int offset = bvOffset + accOffset;

        long byteLen = (long)count * components * componentSize;
        if (count < 0 || offset < 0 || byteLen < 0 || offset + byteLen > binLength)
            throw new GlbParseException("Accessor extends beyond BIN chunk.");

        return (offset, count, componentSize, components);
    }

    static GlbAnimation[]? ReadAnimations(JsonElement root, byte[] bin, GlbBone[]? bones, GlbExtras? extras)
    {
        if (!root.TryGetProperty("animations", out var animsEl) || animsEl.GetArrayLength() == 0)
            return null;
        if (bones == null) return null;

        int boneCount = bones.Length;
        var nodeToBone = new Dictionary<int, int>();
        var skin = root.GetProperty("skins")[0];
        var joints = skin.GetProperty("joints");
        for (int i = 0; i < joints.GetArrayLength(); i++) nodeToBone[joints[i].GetInt32()] = i;

        var result = new List<GlbAnimation>();
        foreach (var animEl in animsEl.EnumerateArray())
        {
            string name = animEl.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            var samplers = animEl.GetProperty("samplers");
            var channels = animEl.GetProperty("channels");

            var samplerData = new (float[] Times, float[] Values, string Path)[samplers.GetArrayLength()];
            for (int s = 0; s < samplers.GetArrayLength(); s++)
            {
                var sm = samplers[s];
                string interp = sm.TryGetProperty("interpolation", out var ip) ? ip.GetString() ?? "LINEAR" : "LINEAR";
                if (interp != "LINEAR")
                    throw new GlbParseException(
                        $"Animation '{name}' uses {interp} interpolation; only LINEAR is supported.");

                samplerData[s] = (
                    ReadAccessorFloats(root, bin, sm.GetProperty("input").GetInt32()),
                    ReadAccessorFloats(root, bin, sm.GetProperty("output").GetInt32()),
                    "");
            }

            // Bind sampler index to channel.target.path so we know which TRS each one is for.
            var channelInfo = new (int SamplerIdx, int BoneIdx, string Path)[channels.GetArrayLength()];
            float duration = 0;
            for (int c = 0; c < channels.GetArrayLength(); c++)
            {
                var ch = channels[c];
                int samplerIdx = ch.GetProperty("sampler").GetInt32();
                int targetNode = ch.GetProperty("target").GetProperty("node").GetInt32();
                string path = ch.GetProperty("target").GetProperty("path").GetString() ?? "";
                int boneIdx = nodeToBone.TryGetValue(targetNode, out var bi) ? bi : -1;
                channelInfo[c] = (samplerIdx, boneIdx, path);

                var tt = samplerData[samplerIdx].Times;
                if (tt.Length > 0) duration = MathF.Max(duration, tt[^1]);
            }

            uint frameCount = SelectFrameCount(samplerData, duration, name, extras);

            // Build per-bone tracks by sampling at uniform t = i * duration / (frameCount - 1).
            var tracks = new GlbBoneTrack[boneCount];
            for (int b = 0; b < boneCount; b++)
            {
                tracks[b] = new GlbBoneTrack
                {
                    BoneIndex = b,
                    Translations = new System.Numerics.Vector3[frameCount],
                    Rotations = new System.Numerics.Quaternion[frameCount],
                };
                for (int f = 0; f < frameCount; f++)
                    tracks[b].Rotations[f] = System.Numerics.Quaternion.Identity;
            }

            for (int c = 0; c < channelInfo.Length; c++)
            {
                var (samplerIdx, boneIdx, path) = channelInfo[c];
                if (boneIdx < 0) continue;
                var (times, values, _) = samplerData[samplerIdx];

                // Output frames are sampled at strictly increasing times, so the keyframe cursor
                // only advances forward across the inner loop.
                int hint = 1;
                for (int f = 0; f < frameCount; f++)
                {
                    float t = frameCount > 1 ? duration * f / (frameCount - 1) : 0;
                    if (path == "translation")
                        tracks[boneIdx].Translations[f] = SampleVec3(times, values, t, ref hint);
                    else if (path == "rotation")
                        tracks[boneIdx].Rotations[f] = SampleQuat(times, values, t, ref hint);
                    // scale ignored (always identity for our exports)
                }
            }

            result.Add(new GlbAnimation
            {
                Name = name,
                Tracks = tracks,
                Duration = duration,
                FrameCount = frameCount,
            });
        }
        return result.ToArray();
    }

    static uint SelectFrameCount(
        (float[] Times, float[] Values, string Path)[] samplers, float duration,
        string animName, GlbExtras? extras)
    {
        // 1. extras override
        if (extras != null && extras.Tma.TryGetValue(animName, out var tmaSection) && tmaSection.OriginalFrameCount > 0)
            return tmaSection.OriginalFrameCount;

        // 2. infer from longest uniform sampler
        int bestN = 0;
        foreach (var s in samplers)
        {
            if (s.Times.Length < 2) continue;
            float dt = s.Times[1] - s.Times[0];
            bool uniform = true;
            for (int k = 2; k < s.Times.Length; k++)
            {
                float gap = s.Times[k] - s.Times[k - 1];
                if (MathF.Abs(gap - dt) > 1e-4f) { uniform = false; break; }
            }
            if (uniform && s.Times.Length > bestN) bestN = s.Times.Length;
        }
        if (bestN > 0) return (uint)bestN;

        // 3. 30 fps fallback
        return (uint)Math.Max(1, MathF.Ceiling(duration * 30));
    }

    // hint is the keyframe cursor; callers that sample at monotonically increasing t pass the same
    // ref hint across calls so we never scan keys we've already passed.
    static System.Numerics.Vector3 SampleVec3(float[] times, float[] values, float t, ref int hint)
    {
        if (times.Length == 0) return System.Numerics.Vector3.Zero;
        if (times.Length == 1 || t <= times[0])
            return new System.Numerics.Vector3(values[0], values[1], values[2]);
        if (t >= times[^1])
        {
            int b = (times.Length - 1) * 3;
            return new System.Numerics.Vector3(values[b], values[b + 1], values[b + 2]);
        }
        if (hint < 1) hint = 1;
        while (hint < times.Length && times[hint] < t) hint++;
        int lo = hint - 1;
        float a = (t - times[lo]) / (times[hint] - times[lo]);
        int li = lo * 3, ri = hint * 3;
        return System.Numerics.Vector3.Lerp(
            new System.Numerics.Vector3(values[li], values[li + 1], values[li + 2]),
            new System.Numerics.Vector3(values[ri], values[ri + 1], values[ri + 2]), a);
    }

    static System.Numerics.Quaternion SampleQuat(float[] times, float[] values, float t, ref int hint)
    {
        if (times.Length == 0) return System.Numerics.Quaternion.Identity;
        if (times.Length == 1 || t <= times[0])
            return new System.Numerics.Quaternion(values[0], values[1], values[2], values[3]);
        if (t >= times[^1])
        {
            int b = (times.Length - 1) * 4;
            return new System.Numerics.Quaternion(values[b], values[b + 1], values[b + 2], values[b + 3]);
        }
        if (hint < 1) hint = 1;
        while (hint < times.Length && times[hint] < t) hint++;
        int lo = hint - 1;
        float a = (t - times[lo]) / (times[hint] - times[lo]);
        int li = lo * 4, ri = hint * 4;
        var q1 = new System.Numerics.Quaternion(values[li], values[li + 1], values[li + 2], values[li + 3]);
        var q2 = new System.Numerics.Quaternion(values[ri], values[ri + 1], values[ri + 2], values[ri + 3]);
        return System.Numerics.Quaternion.Slerp(q1, q2, a);
    }

    static GlbMaterial[] ReadMaterials(JsonElement root, byte[] bin)
    {
        if (!root.TryGetProperty("materials", out var matsEl)) return [];

        var materials = new List<GlbMaterial>();
        foreach (var m in matsEl.EnumerateArray())
        {
            string name = SuffixRegex.Replace(m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", "");
            byte[]? baseColor = null, normalMap = null;

            if (m.TryGetProperty("pbrMetallicRoughness", out var pbr) &&
                pbr.TryGetProperty("baseColorTexture", out var bct))
                baseColor = ReadTextureBytes(root, bin, bct.GetProperty("index").GetInt32());

            if (m.TryGetProperty("normalTexture", out var nt))
                normalMap = ReadTextureBytes(root, bin, nt.GetProperty("index").GetInt32());

            materials.Add(new GlbMaterial { Name = name, BaseColorPng = baseColor, NormalMapPng = normalMap });
        }
        return materials.ToArray();
    }

    static byte[]? ReadTextureBytes(JsonElement root, byte[] bin, int textureIdx)
    {
        var tex = root.GetProperty("textures")[textureIdx];
        if (!tex.TryGetProperty("source", out var src)) return null;
        var image = root.GetProperty("images")[src.GetInt32()];
        if (!image.TryGetProperty("bufferView", out var bvIdx)) return null;

        var bv = root.GetProperty("bufferViews")[bvIdx.GetInt32()];
        int offset = bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        int length = bv.GetProperty("byteLength").GetInt32();
        return bin.AsSpan(offset, length).ToArray();
    }

    static System.Numerics.Matrix4x4 ComputeSceneRootTransform(JsonElement root)
    {
        if (!root.TryGetProperty("scene", out var sceneIdx)) return System.Numerics.Matrix4x4.Identity;
        var scene = root.GetProperty("scenes")[sceneIdx.GetInt32()];
        if (!scene.TryGetProperty("nodes", out var sceneNodes) || sceneNodes.GetArrayLength() != 1)
            return System.Numerics.Matrix4x4.Identity;

        var nodes = root.GetProperty("nodes");
        int nodeIdx = sceneNodes[0].GetInt32();

        // Walk down: root scene node may carry a transform (Blender typically applies a 90deg X rotation here).
        // We bake transforms accumulated from scene root down to the mesh node.
        var accum = System.Numerics.Matrix4x4.Identity;
        var visited = new HashSet<int>();
        int current = nodeIdx;
        while (true)
        {
            if (!visited.Add(current)) break; // safety
            var node = nodes[current];
            var local = System.Numerics.Matrix4x4.Identity;

            if (node.TryGetProperty("matrix", out var mEl))
            {
                float[] mf = new float[16];
                int i = 0;
                foreach (var v in mEl.EnumerateArray()) mf[i++] = v.GetSingle();
                local = new System.Numerics.Matrix4x4(
                    mf[0], mf[1], mf[2], mf[3],
                    mf[4], mf[5], mf[6], mf[7],
                    mf[8], mf[9], mf[10], mf[11],
                    mf[12], mf[13], mf[14], mf[15]);
            }
            else
            {
                var t = ReadVec3(node, "translation", System.Numerics.Vector3.Zero);
                var r = ReadQuat(node, "rotation", System.Numerics.Quaternion.Identity);
                var s = ReadVec3(node, "scale", System.Numerics.Vector3.One);
                local = System.Numerics.Matrix4x4.CreateScale(s)
                      * System.Numerics.Matrix4x4.CreateFromQuaternion(r)
                      * System.Numerics.Matrix4x4.CreateTranslation(t);
            }
            accum = local * accum;

            if (node.TryGetProperty("mesh", out _)) break;
            if (!node.TryGetProperty("children", out var children) || children.GetArrayLength() == 0) break;
            current = children[0].GetInt32();
        }
        return accum;
    }

    static GlbMesh BakeRootTransformIntoMesh(GlbMesh mesh, System.Numerics.Matrix4x4 m)
    {
        if (m.IsIdentity) return mesh;
        var prims = new GlbMeshPrimitive[mesh.Primitives.Length];
        for (int p = 0; p < mesh.Primitives.Length; p++)
        {
            var src = mesh.Primitives[p];
            int vc = src.Positions.Length / 3;

            var pos = new float[src.Positions.Length];
            var nrm = new float[src.Normals.Length];
            var tan = new float[src.Tangents.Length];

            System.Numerics.Matrix4x4.Invert(m, out var mInv);
            var nrmMat = System.Numerics.Matrix4x4.Transpose(mInv);

            for (int i = 0; i < vc; i++)
            {
                var v = new System.Numerics.Vector3(src.Positions[i * 3], src.Positions[i * 3 + 1], src.Positions[i * 3 + 2]);
                var vp = System.Numerics.Vector3.Transform(v, m);
                pos[i * 3] = vp.X; pos[i * 3 + 1] = vp.Y; pos[i * 3 + 2] = vp.Z;

                var n = new System.Numerics.Vector3(src.Normals[i * 3], src.Normals[i * 3 + 1], src.Normals[i * 3 + 2]);
                var np = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.TransformNormal(n, nrmMat));
                nrm[i * 3] = np.X; nrm[i * 3 + 1] = np.Y; nrm[i * 3 + 2] = np.Z;

                var t = new System.Numerics.Vector3(src.Tangents[i * 4], src.Tangents[i * 4 + 1], src.Tangents[i * 4 + 2]);
                var tp = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.TransformNormal(t, m));
                tan[i * 4] = tp.X; tan[i * 4 + 1] = tp.Y; tan[i * 4 + 2] = tp.Z;
                tan[i * 4 + 3] = src.Tangents[i * 4 + 3];
            }

            prims[p] = new GlbMeshPrimitive
            {
                MaterialName = src.MaterialName,
                Positions = pos, Normals = nrm, Tangents = tan,
                TexCoords = src.TexCoords, Indices = src.Indices,
                JointIndices = src.JointIndices, JointWeights = src.JointWeights,
            };
        }
        return new GlbMesh { Primitives = prims };
    }

    static GlbBone[]? BakeRootTransformIntoBones(GlbBone[]? bones, System.Numerics.Matrix4x4 m)
    {
        if (bones == null || m.IsIdentity) return bones;
        // Premultiply root bones' local matrix by m (root bones have ParentIndex == -1).
        var result = new GlbBone[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].ParentIndex >= 0) { result[i] = bones[i]; continue; }
            var local = MatrixDecomp.ColMajorToMatrix(bones[i].LocalMatrix);
            var baked = local * m;
            result[i] = new GlbBone
            {
                Name = bones[i].Name,
                ParentIndex = bones[i].ParentIndex,
                LocalMatrix = MatrixToColumnMajor(baked),
                InverseBindMatrix = bones[i].InverseBindMatrix,
            };
        }
        return result;
    }



    static float[] MatrixToColumnMajor(System.Numerics.Matrix4x4 m) =>
        [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
         m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    static (JsonDocument Json, byte[] Bin) ParseContainer(ReadOnlyMemory<byte> glb)
    {
        var span = glb.Span;
        if (span.Length < 12)
            throw new GlbParseException("GLB shorter than 12-byte header.");

        if (BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != GlbMagic)
            throw new GlbParseException("Not a GLB file (magic number mismatch).");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, 4));
        if (version != 2)
            throw new GlbParseException($"Unsupported glTF version {version} (expected 2).");

        int offset = 12;
        // JSON chunk
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        uint jsonType = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4));
        if (jsonType != ChunkTypeJson)
            throw new GlbParseException("First chunk is not JSON.");
        offset += 8;
        if (offset + (int)jsonLen > span.Length)
            throw new GlbParseException("JSON chunk length exceeds file size.");
        var jsonBytes = glb.Slice(offset, (int)jsonLen);
        offset += (int)jsonLen;

        // Optional BIN chunk
        byte[] bin = [];
        if (offset + 8 <= span.Length)
        {
            uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
            uint binType = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4));
            if (binType != ChunkTypeBin)
                throw new GlbParseException("Second chunk is not BIN.");
            offset += 8;
            if (offset + (int)binLen > span.Length)
                throw new GlbParseException("BIN chunk length exceeds file size.");
            bin = glb.Slice(offset, (int)binLen).ToArray();
        }

        var json = JsonDocument.Parse(jsonBytes);
        return (json, bin);
    }
}
