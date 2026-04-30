using CryBar.Export;
using CryBar.TMM;

namespace CryBar.Tests;

public class TmmWriterTests
{
    [Fact]
    public void Write_SkinnedModel_SkinWeightsAreStartPadded()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "m",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1],
                        Indices = [0, 1, 2],
                        JointIndices = [0, 1, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0],
                        JointWeights = [0.5f, 0.5f, 0, 0,  1, 0, 0, 0,  1, 0, 0, 0],
                    }
                ]
            },
            Bones =
            [
                new GlbBone { Name = "root", LocalMatrix = MatrixDecomp.Compose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One), InverseBindMatrix = Identity16(), ParentIndex = -1 },
                new GlbBone { Name = "child", LocalMatrix = MatrixDecomp.Compose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One), InverseBindMatrix = Identity16(), ParentIndex = 0 },
            ],
            Materials = [new GlbMaterial { Name = "m" }],
        };

        var (tmm, data, _) = TmmWriter.Write(model);
        var parsed = new TmmFile(tmm);
        Assert.True(parsed.FullyParsed);
        var dataFile = new TmmDataFile(data, parsed);
        Assert.True(dataFile.Parsed);
        Assert.NotNull(dataFile.SkinWeights);

        // Vertex 0 had weights [0.5, 0.5, 0, 0]: zeros prepended -> [0, 0, 127, 128] (sum 255).
        var w0 = dataFile.SkinWeights![0];
        Assert.Equal(0, w0.Weight0); Assert.Equal(0, w0.Weight1);
        Assert.True(w0.Weight2 + w0.Weight3 == 255);
        Assert.Equal(0, w0.BoneIndex0); Assert.Equal(0, w0.BoneIndex1);
    }

    [Fact]
    public void Write_TwoBoneSkeleton_BoneRecordsRoundTrip()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Bones =
            [
                new GlbBone { Name = "root", ParentIndex = -1,
                    LocalMatrix = Identity16(),
                    InverseBindMatrix = Identity16() },
                new GlbBone { Name = "child", ParentIndex = 0,
                    LocalMatrix = TranslationMatrix(0, 1, 0),
                    InverseBindMatrix = TranslationMatrix(0, -1, 0) },
            ],
        };

        var (tmm, _, _) = TmmWriter.Write(model);
        var parsed = new TmmFile(tmm);
        Assert.True(parsed.FullyParsed);
        Assert.Equal(2, parsed.Bones!.Length);
        Assert.Equal("root", parsed.Bones[0].Name);
        Assert.Equal("child", parsed.Bones[1].Name);
        Assert.Equal(-1, parsed.Bones[0].ParentId);
        Assert.Equal(0, parsed.Bones[1].ParentId);
    }

    [Fact]
    public void Write_WithLossySections_EmitsWarnings()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Extras = new GlbExtras
            {
                Tmm = new GlbExtras.TmmSection { LossySections = new List<string> { "destruction", "physics" } }
            },
        };
        var (_, _, warnings) = TmmWriter.Write(model);
        Assert.Contains(warnings, w => w.Contains("destruction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, w => w.Contains("physics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Write_WithExtrasAttachments_PopulatesAttachmentRecords()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Attachments = [ new GlbAttachment { Name = "spear_tip", Index = 0, ParentBoneIndex = 0,
                                                LocalMatrix = TranslationMatrix(1, 2, 3) } ],
            Bones = [ new GlbBone { Name = "root", ParentIndex = -1,
                                    LocalMatrix = Identity16(), InverseBindMatrix = Identity16() } ],
            Extras = new GlbExtras
            {
                Tmm = new GlbExtras.TmmSection
                {
                    Attachments =
                    [
                        new GlbExtras.AttachmentEntry
                        {
                            Name = "spear_tip", TypeFlag = 5, ForcedDummyBoneName = "tip",
                            FrameLimit = 10, FramePosition = 0.5f,
                        }
                    ]
                }
            },
        };
        var (tmm, _, _) = TmmWriter.Write(model);
        var parsed = new TmmFile(tmm);
        Assert.True(parsed.FullyParsed);
        var atts = parsed.Attachments!;
        Assert.Single(atts);
        Assert.Equal(5u, atts[0].TypeFlag);
        Assert.Equal("tip", atts[0].ForcedDummyBoneName);
        Assert.Equal(10, atts[0].FrameLimit);
    }

    [Fact]
    public void Write_SelfValidates_ProducesParseableOutputForValidInput()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
        };
        var ex = Record.Exception(() => TmmWriter.Write(model));
        Assert.Null(ex);
    }

    static float[] Identity16() => [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];

    static float[] TranslationMatrix(float x, float y, float z)
    {
        var m = (float[])Identity16().Clone();
        m[12] = x; m[13] = y; m[14] = z;
        return m;
    }

    [Fact]
    public void Write_NullExtrasOnSkinnedModel_EmitsThreeFallbackWarnings()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "m",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1],
                        Indices = [0, 1, 2],
                        JointIndices = [0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0],
                        JointWeights = [1, 0, 0, 0,  1, 0, 0, 0,  1, 0, 0, 0],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "m" }],
            Bones =
            [
                new GlbBone
                {
                    Name = "root", ParentIndex = -1,
                    LocalMatrix = TmmTestHelpers.Identity16Local(),
                    InverseBindMatrix = TmmTestHelpers.Identity16Local()
                }
            ],
        };

        var (_, _, warnings) = TmmWriter.Write(model);

        Assert.Contains(warnings, w => w.Contains("main_matrix", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("extended_bbox", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("auto_attach", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_NonSkinnedModelWithoutExtras_DoesNotWarnAboutExtendedBboxOrAutoAttach()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "m",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1],
                        Indices = [0, 1, 2],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "m" }],
        };

        var (_, _, warnings) = TmmWriter.Write(model);

        Assert.Contains(warnings, w => w.Contains("main_matrix", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("extended_bbox", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("auto_attach", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_ExtrasPresent_NoFallbackWarnings()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "m",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1],
                        Indices = [0, 1, 2],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "m" }],
            Extras = new GlbExtras
            {
                Tmm = new GlbExtras.TmmSection
                {
                    MainMatrix = [1, 0, 0, 0,  0, 1, 0, 0,  0, 0, 1, 0,  0, 0, 0, 1],
                    ExtendedBbox = [-1, -1, -1, 1, 1, 1],
                    AutoAttach = new GlbExtras.AutoAttachInfo(),
                }
            },
        };

        var (_, _, warnings) = TmmWriter.Write(model);

        Assert.DoesNotContain(warnings, w => w.Contains("main_matrix", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("extended_bbox", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("auto_attach", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_EmptyModel_ProducesParseableTmm()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
        };

        var (tmm, data, warnings) = TmmWriter.Write(model);

        Assert.NotNull(tmm);
        Assert.NotNull(data);
        var parsed = new TmmFile(tmm);
        Assert.True(parsed.ParseHeader());
        Assert.Equal(37u, parsed.Version);
    }

    [Fact]
    public void Write_SingleQuad_VertexBufferRoundTripsThroughTmmFileParse()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "m",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0,  0, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1,  0, 1],
                        Indices = [0, 1, 2,  0, 2, 3],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "m" }],
        };

        var (tmm, data, _) = TmmWriter.Write(model);

        var parsed = new TmmFile(tmm);
        Assert.True(parsed.ParseHeader());
        Assert.True(parsed.FullyParsed);
        Assert.Equal(4u, parsed.NumVertices);
        Assert.Equal(6u, parsed.NumTriangleVerts);

        var dataFile = new TmmDataFile(data, parsed);
        Assert.True(dataFile.Parsed);
        Assert.Equal(4, dataFile.Vertices!.Length);
        Assert.Equal(6, dataFile.Indices!.Length);
    }
}
