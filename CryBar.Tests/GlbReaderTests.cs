using System.Text;
using CryBar.Export;
using CryBar.TMM;
using static CryBar.Tests.TmmTestHelpers;

namespace CryBar.Tests;

public class GlbReaderTests
{
    [Fact]
    public void ParseContainer_ValidGlb_ReturnsJsonAndBin()
    {
        var (tmm, dataFile) = CreateMinimalModel();
        var glb = GlbExporter.ExportGlb(tmm, dataFile)!;

        var (json, bin) = GlbReader.ParseContainerForTests(glb);
        Assert.True(json.RootElement.TryGetProperty("asset", out _));
        Assert.True(bin.Length > 0);
    }

    [Fact]
    public void ParseContainer_BadMagic_Throws()
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes("BAD!", bytes.AsSpan(0, 4));
        Assert.Throws<GlbParseException>(() => GlbReader.ParseContainerForTests(bytes));
    }

    [Fact]
    public void ParseContainer_TooShort_Throws()
    {
        Assert.Throws<GlbParseException>(() => GlbReader.ParseContainerForTests(new byte[8]));
    }

    [Fact]
    public void Parse_MinimalGlb_ReturnsGlbModelWithExtras()
    {
        var (tmm, dataFile) = CreateMinimalModel();
        var extras = new GlbExtras { Tmm = new GlbExtras.TmmSection { AutoBurnMode = 7 } };
        var glb = GlbExporter.ExportGlb(tmm, dataFile, extras: extras)!;

        var model = GlbReader.Parse(glb);
        Assert.NotNull(model);
        Assert.NotNull(model.Mesh);
        Assert.NotNull(model.Extras);
        Assert.Equal((byte)7, model.Extras!.Tmm.AutoBurnMode);
    }

    [Fact]
    public void Parse_MinimalGlb_HasOnePrimitiveWithVerticesAndTriangles()
    {
        var (tmm, dataFile) = CreateMinimalModel();
        var glb = GlbExporter.ExportGlb(tmm, dataFile)!;

        var model = GlbReader.Parse(glb);
        Assert.Single(model.Mesh.Primitives);

        var prim = model.Mesh.Primitives[0];
        Assert.Equal(3 * 3, prim.Positions.Length);          // 3 verts * 3 floats
        Assert.Equal(3 * 3, prim.Normals.Length);
        Assert.Equal(3 * 4, prim.Tangents.Length);
        Assert.Equal(3 * 2, prim.TexCoords.Length);
        Assert.Equal(3u, (uint)prim.Indices.Length);
    }

    [Fact]
    public void Parse_GlbWithAnimation_ReturnsResampledTrackPerBone()
    {
        var tmm = CreateSyntheticTmmFile(numVertices: 3, numTriangleVerts: 3, hasSkinning: true,
            numMeshGroups: 1, materials: ["m"], submodels: ["default"], numBones: 2);
        var dataBytes = CreateSyntheticData(numVertices: 3, numTriangleVerts: 3, hasSkinning: true);
        var dataFile = new TmmDataFile(dataBytes, tmm);

        var anim = new GlbExporter.GlbAnimation
        {
            Name = "idle",
            Tracks = SyntheticTracksUniform(boneCount: 2, frameCount: 30, duration: 1.0f),
            Duration = 1.0f,
            FrameCount = 30,
        };
        var glb = GlbExporter.ExportGlb(tmm, dataFile, animations: [anim])!;

        var model = GlbReader.Parse(glb);
        Assert.NotNull(model.Animations);
        Assert.Single(model.Animations!);

        var a = model.Animations![0];
        Assert.Equal("idle", a.Name);
        Assert.Equal(30u, a.FrameCount);
        Assert.Equal(2, a.Tracks.Length);
        Assert.Equal(30, a.Tracks[0].Translations.Length);
        Assert.Equal(30, a.Tracks[0].Rotations.Length);
    }

    [Fact]
    public void Parse_GlbWithBones_ReturnsParentIndexedBoneArray()
    {
        var tmm = CreateSyntheticTmmFile(numVertices: 3, numTriangleVerts: 3, hasSkinning: true,
            numMeshGroups: 1, materials: ["m"], submodels: ["default"], numBones: 3);
        var dataBytes = CreateSyntheticData(numVertices: 3, numTriangleVerts: 3, hasSkinning: true);
        var dataFile = new TmmDataFile(dataBytes, tmm);
        var glb = GlbExporter.ExportGlb(tmm, dataFile)!;

        var model = GlbReader.Parse(glb);
        Assert.NotNull(model.Bones);
        Assert.Equal(3, model.Bones!.Length);

        Assert.Equal(-1, model.Bones[0].ParentIndex);
        Assert.Equal(0, model.Bones[1].ParentIndex);
        Assert.Equal(1, model.Bones[2].ParentIndex);

        Assert.Equal(16, model.Bones[0].LocalMatrix.Length);
        Assert.Equal(16, model.Bones[0].InverseBindMatrix.Length);
    }

    [Fact]
    public void Parse_GlbWithMaterial_ExtractsPngBytes()
    {
        var (tmm, dataFile) = CreateMinimalModel();
        var pngBytes = CreateTinyRgbaPng(2, 2);

        var glbMaterials = new[]
        {
            new GlbExporter.GlbMaterial
            {
                Name = "body",
                BaseColorPng = pngBytes,
                NormalMapPng = pngBytes,
            }
        };
        var glb = GlbExporter.ExportGlb(tmm, dataFile, materials: glbMaterials)!;

        var model = GlbReader.Parse(glb);
        Assert.Single(model.Materials);
        Assert.Equal("body", model.Materials[0].Name);
        Assert.NotNull(model.Materials[0].BaseColorPng);
        Assert.NotNull(model.Materials[0].NormalMapPng);
    }

    [Fact]
    public void Parse_GlbWithMeshNodeMainMatrix_OverridesAssetLevel()
    {
        var (tmm, dataFile) = CreateMinimalModel();
        var extras = new GlbExtras
        {
            Tmm = new GlbExtras.TmmSection
            {
                MainMatrix = [2, 0, 0, 0,  0, 2, 0, 0,  0, 0, 2, 0,  0, 0, 0, 1],
            },
        };
        var glb = GlbExporter.ExportGlb(tmm, dataFile, extras: extras)!;

        var model = GlbReader.Parse(glb);
        Assert.NotNull(model.Extras);
        Assert.Equal(2.0f, model.Extras!.Tmm.MainMatrix[0]);
    }

    [Fact]
    public void Parse_GlbWithAttachments_ReturnsAttachmentArrayWithIndex()
    {
        var tmm = CreateSyntheticTmmFile(numVertices: 3, numTriangleVerts: 3, hasSkinning: true,
            numMeshGroups: 1, materials: ["m"], submodels: ["default"], numBones: 1, numAttachments: 2);
        var dataBytes = CreateSyntheticData(numVertices: 3, numTriangleVerts: 3, hasSkinning: true);
        var dataFile = new TmmDataFile(dataBytes, tmm);
        var glb = GlbExporter.ExportGlb(tmm, dataFile, extras: new GlbExtras())!;

        var model = GlbReader.Parse(glb);
        Assert.NotNull(model.Attachments);
        Assert.Equal(2, model.Attachments!.Length);
        Assert.Equal(0, model.Attachments[0].Index);
        Assert.Equal(1, model.Attachments[1].Index);
    }

    [Fact]
    public void Parse_NameSuffixStripping_RemovesDotNNN()
    {
        Assert.Equal("Bone", GlbReader.StripSuffixForTests("Bone.001"));
        Assert.Equal("Bone", GlbReader.StripSuffixForTests("Bone.123"));
        Assert.Equal("Bone.1234", GlbReader.StripSuffixForTests("Bone.1234"));
        Assert.Equal("Bone", GlbReader.StripSuffixForTests("Bone"));
        Assert.Equal("", GlbReader.StripSuffixForTests(""));
    }

    [Theory]
    [InlineData("POSITION")]
    [InlineData("NORMAL")]
    [InlineData("TANGENT")]
    [InlineData("TEXCOORD_0")]
    public void Parse_PrimitiveMissingRequiredAttribute_ThrowsClearError(string missing)
    {
        var attrs = new Dictionary<string, int>
        {
            ["POSITION"] = 0, ["NORMAL"] = 1, ["TANGENT"] = 2, ["TEXCOORD_0"] = 3,
        };
        attrs.Remove(missing);
        var attrsJson = "{" + string.Join(",", attrs.Select(kv => $"\"{kv.Key}\":{kv.Value}")) + "}";

        string json = $$"""
        {
          "asset": {"version":"2.0"},
          "scene": 0,
          "scenes": [{"nodes":[0]}],
          "nodes": [{"mesh":0}],
          "meshes": [{"primitives":[{"attributes":{{attrsJson}},"indices":4,"material":0}]}],
          "materials": [{"name":"m"}],
          "accessors": [
            {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":2,"componentType":5126,"count":3,"type":"VEC4"},
            {"bufferView":3,"componentType":5126,"count":3,"type":"VEC2"},
            {"bufferView":4,"componentType":5125,"count":3,"type":"SCALAR"}
          ],
          "bufferViews":[
            {"buffer":0,"byteOffset":0,"byteLength":36},
            {"buffer":0,"byteOffset":36,"byteLength":36},
            {"buffer":0,"byteOffset":72,"byteLength":48},
            {"buffer":0,"byteOffset":120,"byteLength":24},
            {"buffer":0,"byteOffset":144,"byteLength":12}
          ],
          "buffers":[{"byteLength":156}]
        }
        """;
        byte[] glb = AssembleGlb(json, BuildBin_3VertsScaledTest());

        var ex = Assert.Throws<GlbParseException>(() => GlbReader.Parse(glb));
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void Parse_PrimitiveMissingIndices_ThrowsClearError()
    {
        string json = """
        {
          "asset": {"version":"2.0"},
          "scene": 0,
          "scenes": [{"nodes":[0]}],
          "nodes": [{"mesh":0}],
          "meshes": [{"primitives":[{
            "attributes":{"POSITION":0,"NORMAL":1,"TANGENT":2,"TEXCOORD_0":3},
            "material":0
          }]}],
          "materials": [{"name":"m"}],
          "accessors": [
            {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":2,"componentType":5126,"count":3,"type":"VEC4"},
            {"bufferView":3,"componentType":5126,"count":3,"type":"VEC2"}
          ],
          "bufferViews":[
            {"buffer":0,"byteOffset":0,"byteLength":36},
            {"buffer":0,"byteOffset":36,"byteLength":36},
            {"buffer":0,"byteOffset":72,"byteLength":48},
            {"buffer":0,"byteOffset":120,"byteLength":24}
          ],
          "buffers":[{"byteLength":144}]
        }
        """;
        byte[] glb = AssembleGlb(json, BuildBin_3VertsScaledTest());

        var ex = Assert.Throws<GlbParseException>(() => GlbReader.Parse(glb));
        Assert.Contains("indices", ex.Message);
    }

    [Fact]
    public void Parse_NonIdentitySceneRootTransform_BakedIntoVerticesAndBones()
    {
        string json = """
        {
          "asset": {"version":"2.0"},
          "scene": 0,
          "scenes": [{"nodes":[0]}],
          "nodes": [
            {"name":"root","scale":[2,2,2],"children":[1]},
            {"mesh":0}
          ],
          "meshes": [{"primitives":[{
            "attributes":{"POSITION":0,"NORMAL":1,"TANGENT":2,"TEXCOORD_0":3},
            "indices":4,"material":0
          }]}],
          "materials": [{"name":"m"}],
          "accessors": [
            {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":2,"componentType":5126,"count":3,"type":"VEC4"},
            {"bufferView":3,"componentType":5126,"count":3,"type":"VEC2"},
            {"bufferView":4,"componentType":5125,"count":3,"type":"SCALAR"}
          ],
          "bufferViews":[
            {"buffer":0,"byteOffset":0,"byteLength":36},
            {"buffer":0,"byteOffset":36,"byteLength":36},
            {"buffer":0,"byteOffset":72,"byteLength":48},
            {"buffer":0,"byteOffset":120,"byteLength":24},
            {"buffer":0,"byteOffset":144,"byteLength":12}
          ],
          "buffers":[{"byteLength":156}]
        }
        """;
        byte[] glb = AssembleGlb(json, BuildBin_3VertsScaledTest());

        var model = GlbReader.Parse(glb);
        var prim = model.Mesh.Primitives[0];
        // Source positions were [1,0,0], [0,1,0], [0,0,1]; scale=2 should produce [2,0,0], [0,2,0], [0,0,2].
        Assert.Equal(2.0f, prim.Positions[0]);
        Assert.Equal(2.0f, prim.Positions[4]);
        Assert.Equal(2.0f, prim.Positions[8]);
    }

    static byte[] AssembleGlb(string json, byte[] bin)
    {
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        while (jsonBytes.Length % 4 != 0) jsonBytes = [.. jsonBytes, (byte)' '];
        var binPad = bin;
        while (binPad.Length % 4 != 0) binPad = [.. binPad, (byte)0];

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + jsonBytes.Length + 8 + binPad.Length));
        w.Write((uint)jsonBytes.Length); w.Write(0x4E4F534Au); w.Write(jsonBytes);
        w.Write((uint)binPad.Length); w.Write(0x004E4942u); w.Write(binPad);
        return ms.ToArray();
    }

    static byte[] BuildBin_3VertsScaledTest()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        // POSITION (3 vec3): [1,0,0], [0,1,0], [0,0,1]
        w.Write(1f); w.Write(0f); w.Write(0f);
        w.Write(0f); w.Write(1f); w.Write(0f);
        w.Write(0f); w.Write(0f); w.Write(1f);
        // NORMAL (3 vec3): [0,1,0] x 3
        for (int i = 0; i < 3; i++) { w.Write(0f); w.Write(1f); w.Write(0f); }
        // TANGENT (3 vec4): [1,0,0,1] x 3
        for (int i = 0; i < 3; i++) { w.Write(1f); w.Write(0f); w.Write(0f); w.Write(1f); }
        // TEXCOORD_0 (3 vec2): [0,0] x 3
        for (int i = 0; i < 3; i++) { w.Write(0f); w.Write(0f); }
        // INDICES (3 u32): 0, 1, 2
        w.Write(0u); w.Write(1u); w.Write(2u);
        return ms.ToArray();
    }

    static byte[] CreateTinyRgbaPng(int w, int h)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(w, h);
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return ms.ToArray();
    }
}
