using CryBar;
using CryBar.Bar;
using CryBar.Dependencies;
using CryBar.Export;
using CryBar.Indexing;
using CryBar.TMM;
using CryBar.Utilities;
using Xunit;
using static CryBar.Tests.TmmTestHelpers;

namespace CryBar.Tests;

public class GlbRoundTripTests
{
    static readonly string GamePath =
        Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";

    static bool GameInstalled =>
        Directory.Exists(Path.Combine(GamePath, "modelcache")) &&
        File.Exists(Path.Combine(GamePath, "modelcache", "ArtModelCacheMeta.bar"));

    [Fact]
    public void TmaController_RoundTripsThroughExtrasOnly()
    {
        // Bone has identity local matrix so the writer's mirror/unmirror is identity-preserving.
        var bones = new[]
        {
            new GlbBone { Name = "root", ParentIndex = -1,
                LocalMatrix = Identity16Local(),
                InverseBindMatrix = Identity16Local() },
        };

        var anim = new GlbAnimation
        {
            Name = "attack",
            FrameCount = 2,
            Duration = 1.0f,
            Tracks = [new GlbBoneTrack { BoneIndex = 0, Translations = [], Rotations = [] }],
        };

        var section = new GlbExtras.TmaSection
        {
            OriginalFrameCount = 2,
            Controllers =
            [
                new GlbExtras.TmaControllerEntry
                {
                    Type = 1, Start = 0.32f, End = 0.65f, EaseIn = 0.0f, EaseOut = 0.0f,
                    InvertLogic = true, AttachPointName = "arrow",
                },
                new GlbExtras.TmaControllerEntry
                {
                    Type = 2, SpawnTime = 0.5f, FootprintName = "boot_left", FootprintId = 7,
                    InvertTextureY = false, AttachPointName = "LeftFoot", IsRightSide = false,
                },
            ],
        };

        var (tmaBytes, warnings) = TmaWriter.Write(anim, bones, section);
        Assert.Empty(warnings);

        var roundTripped = new TmaFile(tmaBytes);
        Assert.True(roundTripped.Parsed);
        Assert.NotNull(roundTripped.Controllers);
        Assert.Equal(2, roundTripped.Controllers!.Length);

        var vis = Assert.IsType<TmaVisibilityController>(roundTripped.Controllers[0]);
        Assert.Equal(0.32f, vis.Start, 5);
        Assert.Equal(0.65f, vis.End, 5);
        Assert.True(vis.InvertLogic);
        Assert.Equal("arrow", vis.AttachPointName);

        var foot = Assert.IsType<TmaFootprintController>(roundTripped.Controllers[1]);
        Assert.Equal(0.5f, foot.SpawnTime, 5);
        Assert.Equal(7, foot.FootprintId);
        Assert.Equal("boot_left", foot.FootprintName);
        Assert.Equal("LeftFoot", foot.AttachPointName);
        Assert.False(foot.IsRightSide);
    }

    [Fact]
    public async Task FbximportOverride_ReplacesControllersDuringConvert()
    {
        // Synthetic GLB model with one bone and one animation that has no extras controllers.
        // Convert with a fbximport override and verify the resulting TMA carries the controller.
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Bones =
            [
                new GlbBone { Name = "root", ParentIndex = -1,
                    LocalMatrix = Identity16Local(),
                    InverseBindMatrix = Identity16Local() },
            ],
            Animations =
            [
                new GlbAnimation
                {
                    Name = "shoot",
                    FrameCount = 2,
                    Duration = 1.0f,
                    Tracks = [new GlbBoneTrack { BoneIndex = 0, Translations = [], Rotations = [] }],
                }
            ],
            Materials = [],
        };

        var fbxBytes = FbximportEmitter.EmitForTma(
            new GlbExtras.TmaSection
            {
                Controllers =
                [
                    new GlbExtras.TmaControllerEntry
                    {
                        Type = 1, Start = 0.1f, End = 0.9f, InvertLogic = false,
                        AttachPointName = "arrow",
                    },
                ],
            },
            duration: 1.0f);

        var fbxByName = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["shoot"] = fbxBytes,
        };

        var result = await GlbConverter.ConvertAsync(
            model, "synth", new Dictionary<string, GlbConverter.DdtMaterialParams>(),
            progress: null, token: default,
            fbximportByAnimName: fbxByName);

        var tmaFile = result.Files.FirstOrDefault(f => f.Name == "shoot.tma");
        Assert.NotNull(tmaFile);

        var parsed = new TmaFile(tmaFile.Bytes);
        Assert.True(parsed.Parsed);
        Assert.NotNull(parsed.Controllers);
        var vis = Assert.IsType<TmaVisibilityController>(Assert.Single(parsed.Controllers!));
        Assert.Equal(0.1f, vis.Start, 5);
        Assert.Equal(0.9f, vis.End, 5);
        Assert.Equal("arrow", vis.AttachPointName);
    }

    [Fact]
    public void Layer1_SyntheticRoundTrip_StructurallyEquivalent()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "body",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0,  0, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1,  0, 1],
                        Indices = [0, 1, 2,  0, 2, 3],
                        JointIndices = [0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  1, 0, 0, 0],
                        JointWeights = [1, 0, 0, 0,  1, 0, 0, 0,  1, 0, 0, 0,  1, 0, 0, 0],
                    }
                ]
            },
            Bones =
            [
                new GlbBone { Name = "root", ParentIndex = -1,
                    LocalMatrix = Identity16Local(),
                    InverseBindMatrix = Identity16Local() },
                new GlbBone { Name = "child", ParentIndex = 0,
                    LocalMatrix = Identity16Local(),
                    InverseBindMatrix = Identity16Local() },
            ],
            Attachments = [
                new GlbAttachment { Name = "spear_tip", Index = 0, ParentBoneIndex = 1,
                                    LocalMatrix = Identity16Local() }
            ],
            Materials = [new GlbMaterial { Name = "body" }],
            Extras = new GlbExtras
            {
                Tmm = new GlbExtras.TmmSection
                {
                    AutoBurnMode = 3,
                    Raytracing = true,
                    Submodels = ["default"],
                }
            }
        };

        var (tmm, data, warnings) = TmmWriter.Write(model);
        Assert.Empty(warnings);

        var parsed = new TmmFile(tmm);
        Assert.True(parsed.FullyParsed);
        Assert.Equal(4u, parsed.NumVertices);
        Assert.Equal(6u, parsed.NumTriangleVerts);
        Assert.Equal(2, parsed.Bones!.Length);
        Assert.Single(parsed.Attachments!);
        Assert.Single(parsed.Materials!);
        Assert.Equal((byte)3, parsed.AutoBurnMode);
        Assert.True(parsed.EnableRayTracingForModel);

        var dataFile = new TmmDataFile(data, parsed);
        Assert.True(dataFile.Parsed);
        Assert.NotNull(dataFile.Vertices);
        Assert.NotNull(dataFile.Indices);
        Assert.NotNull(dataFile.SkinWeights);
    }

    [Fact]
    public void Layer1b_NullExtrasModel_ProducesValidFallbackDefaults()
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

        var (tmm, _, warnings) = TmmWriter.Write(model);
        Assert.Contains(warnings, w => w.Contains("main_matrix", StringComparison.Ordinal));

        var parsed = new TmmFile(tmm);
        Assert.True(parsed.FullyParsed);
        // Fall-back: autoburn_mode=0, raytracing=false
        Assert.Equal((byte)0, parsed.AutoBurnMode);
        Assert.False(parsed.EnableRayTracingForModel);
        // Affine identity: M11=1, M22=1, M33=1
        Assert.Equal(1f, parsed.MainMatrix![0]);
        Assert.Equal(1f, parsed.MainMatrix[5]);
        Assert.Equal(1f, parsed.MainMatrix[10]);
    }

    [Fact]
    public void TmmWriter_AttachmentMatrices_RoundTripBothSlotsCorrectly()
    {
        // Two distinct 4x3 row-major matrices so slot-swap and transpose bugs are detectable.
        // X = AdjustmentTransformMatrix (slot 1), Y = LocalTransformMatrix (slot 2).
        var X = new float[] {
            2, 0, 0, 1.5f,
            0, 3, 0, -0.5f,
            0, 0, 4, 0.7f
        };
        var Y = new float[] {
            5, 0, 0, 0.1f,
            0, 6, 0, 0.2f,
            0, 0, 7, 0.3f
        };

        // Simulate the GlbExporter forward path on X to produce att.LocalMatrix:
        //   row-major-3x4 -> col-major-4x4 (with [0,0,0,1] homogeneous col)
        //   then F*M*F via AxisNegateMask {1,2,3,4,8,12} (negate flat indices).
        var attLocalMatrix = new float[] {
            X[0], X[4], X[8],  0,
            X[1], X[5], X[9],  0,
            X[2], X[6], X[10], 0,
            X[3], X[7], X[11], 1,
        };
        foreach (var i in new[] { 1, 2, 3, 4, 8, 12 })
            attLocalMatrix[i] = -attLocalMatrix[i];

        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [new GlbMeshPrimitive
            {
                MaterialName = "m",
                Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                TexCoords = [0, 0,  1, 0,  1, 1],
                Indices = [0, 1, 2],
                JointIndices = [0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0],
                JointWeights = [1, 0, 0, 0,  1, 0, 0, 0,  1, 0, 0, 0],
            }] },
            Bones = [new GlbBone {
                Name = "root", ParentIndex = -1,
                LocalMatrix = Identity16Local(),
                InverseBindMatrix = Identity16Local()
            }],
            Attachments = [new GlbAttachment {
                Name = "att1", Index = 0, ParentBoneIndex = 0,
                LocalMatrix = attLocalMatrix
            }],
            Materials = [new GlbMaterial { Name = "m" }],
            Extras = new GlbExtras
            {
                Tmm = new GlbExtras.TmmSection
                {
                    Attachments = [new GlbExtras.AttachmentEntry
                    {
                        Name = "att1",
                        LocalMatrix = (float[])Y.Clone(),
                    }],
                }
            }
        };

        var (tmm, _, _) = TmmWriter.Write(model);
        var parsed = new TmmFile(tmm);
        Assert.True(parsed.FullyParsed);
        Assert.Single(parsed.Attachments!);

        var att = parsed.Attachments![0];
        for (int i = 0; i < 12; i++)
            Assert.Equal(X[i], att.AdjustmentTransformMatrix[i], 4);
        for (int i = 0; i < 12; i++)
            Assert.Equal(Y[i], att.LocalTransformMatrix[i], 4);
    }

    [Fact]
    public void TmmWriter_NonTrivialBoneMatrix_RoundTripsThroughDiskFormat()
    {
        // 60deg Z-rotation + translation. Picks a matrix where transpose != original
        // so the col-major flat write convention is verified.
        var t = new System.Numerics.Vector3(1.5f, -0.7f, 0.3f);
        var r = System.Numerics.Quaternion.CreateFromAxisAngle(
            System.Numerics.Vector3.UnitZ, MathF.PI / 3f);
        var matT = System.Numerics.Matrix4x4.CreateTranslation(t);
        var matR = System.Numerics.Matrix4x4.CreateFromQuaternion(r);
        var mat = matR * matT; // System.Numerics row-vector form

        // Store in CryBar internal flat (= col-major of col-vector form = row-major of row-vector form).
        var input = new float[]
        {
            mat.M11, mat.M12, mat.M13, mat.M14,
            mat.M21, mat.M22, mat.M23, mat.M24,
            mat.M31, mat.M32, mat.M33, mat.M34,
            mat.M41, mat.M42, mat.M43, mat.M44,
        };

        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [new GlbMeshPrimitive
            {
                MaterialName = "m",
                Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                TexCoords = [0, 0,  1, 0,  1, 1],
                Indices = [0, 1, 2],
                JointIndices = [0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0],
                JointWeights = [1, 0, 0, 0,  1, 0, 0, 0,  1, 0, 0, 0],
            }] },
            Bones = [new GlbBone {
                Name = "test_bone",
                ParentIndex = -1,
                LocalMatrix = (float[])input.Clone(),
                InverseBindMatrix = Identity16Local()
            }],
            Materials = [new GlbMaterial { Name = "m" }],
        };

        var (tmm, _, _) = TmmWriter.Write(model);
        var parsed = new TmmFile(tmm);
        Assert.True(parsed.FullyParsed);

        // Expected disk content: F*M*F applied at TRS level, written col-major.
        // F = diag(-1,1,1,1). For a row-vector matrix M_sn, F*M_sn*F flips entries
        // where exactly one of (row,col) is 0: M12, M13, M14, M21, M31, M41.
        var f = new System.Numerics.Matrix4x4(
            mat.M11, -mat.M12, -mat.M13, -mat.M14,
           -mat.M21,  mat.M22,  mat.M23,  mat.M24,
           -mat.M31,  mat.M32,  mat.M33,  mat.M34,
           -mat.M41,  mat.M42,  mat.M43,  mat.M44);
        var expected = new float[]
        {
            f.M11, f.M12, f.M13, f.M14,
            f.M21, f.M22, f.M23, f.M24,
            f.M31, f.M32, f.M33, f.M34,
            f.M41, f.M42, f.M43, f.M44,
        };

        var actual = parsed.Bones![0].ParentSpaceMatrix;
        Assert.Equal(16, actual.Length);
        for (int i = 0; i < 16; i++)
            Assert.Equal(expected[i], actual[i], 4);
    }

    [SkippableFact]
    public async Task Layer2_VanillaTmmRoundTrip_FullPipelineWithEverything()
    {
        Skip.IfNot(GameInstalled,
            "AOM:R game install not found at default Steam path or AOMR_GAME_PATH");

        var modelcacheDir = Path.Combine(GamePath, "modelcache");
        var metaBarPath = Path.Combine(modelcacheDir, "ArtModelCacheMeta.bar");

        // fileIndex covers all modelcache BARs + supplemental (art/, etc.) so .tmm.data and
        // animfiles can be resolved even though they live in different BARs than the TMMs.
        var fileIndex = new FileIndex();
        var modelcacheBars = Directory.GetFiles(modelcacheDir, "*.bar", SearchOption.AllDirectories);
        FileIndexBuilder.IndexBarFiles(fileIndex, modelcacheBars);
        var supplemental = FileIndexBuilder.FindSupplementalBarFiles(modelcacheDir);
        FileIndexBuilder.IndexBarFiles(fileIndex, supplemental);

        using var metaStream = File.OpenRead(metaBarPath);
        var metaBar = new BarFile(metaStream);
        metaBar.Load(out _);

        int processed = 0, succeeded = 0;
        var failed = new List<string>();

        foreach (var entry in metaBar.Entries!)
        {
            if (processed >= 10) break;
            if (!entry.Name.EndsWith(".tmm", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Name.EndsWith(".tmm.data", StringComparison.OrdinalIgnoreCase)) continue;

            // .tmm.data lives in ArtModelCacheModelData*.bar, not in Meta -- look up via fileIndex
            var dataIndexEntries = fileIndex.Find(entry.Name + ".data");
            if (dataIndexEntries.Count == 0) continue;

            try
            {
                using var tmmPooled = entry.ReadDataDecompressedPooled(metaStream);
                if (tmmPooled == null) continue;
                var tmmBytes = tmmPooled.Span.ToArray();

                using var dataBuf = await ReadIndexEntryPooled(dataIndexEntries[0]);
                if (dataBuf == null) continue;
                var dataBytes = dataBuf.Span.ToArray();

                var origTmm = new TmmFile(tmmBytes);
                if (!origTmm.FullyParsed) continue;
                var origData = new TmmDataFile(dataBytes, origTmm);
                if (!origData.Parsed || origData.Vertices == null) continue;

                var tmmStem = Path.GetFileNameWithoutExtension(entry.Name);
                var sourceTmas = await FindTmasForTmm(tmmStem, fileIndex);

                // DDT pairing omitted: DDT round-trip is exercised by DDTImageTests.
                var glbAnimations = sourceTmas.Count > 0
                    ? sourceTmas.Select(t => new GlbExporter.GlbAnimation
                    {
                        Name = t.Name,
                        Tracks = TmaDecoder.DecodeAllTracks(t.Tma)!,
                        Duration = t.Tma.Duration,
                        FrameCount = t.Tma.FrameCount,
                    }).ToList()
                    : null;

                var glbBytes = ConversionHelper.ConvertTmmToGlbBytes(
                    tmmBytes, dataBytes,
                    materials: null,
                    animations: glbAnimations,
                    sourceTmas: sourceTmas.Count > 0 ? sourceTmas : null,
                    sourceDdts: null);
                if (glbBytes == null) { failed.Add($"{entry.Name}: ConvertTmmToGlbBytes returned null"); processed++; continue; }

                var model = GlbReader.Parse(glbBytes);
                var (newTmmBytes, newDataBytes, _) = TmmWriter.Write(model);

                var reparsed = new TmmFile(newTmmBytes);
                if (!reparsed.FullyParsed) { failed.Add($"{entry.Name}: writer output failed re-parse"); processed++; continue; }

                var reparsedData = new TmmDataFile(newDataBytes, reparsed);
                if (!reparsedData.Parsed || reparsedData.Vertices == null)
                { failed.Add($"{entry.Name}: re-parse data failed"); processed++; continue; }

                if (reparsed.NumVertices != origTmm.NumVertices)
                    failed.Add($"{entry.Name}: vert count {reparsed.NumVertices} != {origTmm.NumVertices}");
                if (reparsed.NumTriangleVerts != origTmm.NumTriangleVerts)
                    failed.Add($"{entry.Name}: tri vert count {reparsed.NumTriangleVerts} != {origTmm.NumTriangleVerts}");
                if ((reparsed.Bones?.Length ?? 0) != (origTmm.Bones?.Length ?? 0))
                    failed.Add($"{entry.Name}: bone count {reparsed.Bones?.Length ?? 0} != {origTmm.Bones?.Length ?? 0}");
                if ((reparsed.Attachments?.Length ?? 0) != (origTmm.Attachments?.Length ?? 0))
                    failed.Add($"{entry.Name}: attachment count mismatch");

                if (origTmm.Bones != null && reparsed.Bones != null &&
                    origTmm.Bones.Length == reparsed.Bones.Length)
                {
                    for (int b = 0; b < origTmm.Bones.Length; b++)
                    {
                        if (origTmm.Bones[b].Name != reparsed.Bones[b].Name)
                            failed.Add($"{entry.Name}: bone[{b}] name '{reparsed.Bones[b].Name}' != '{origTmm.Bones[b].Name}'");
                        if (origTmm.Bones[b].ParentId != reparsed.Bones[b].ParentId)
                            failed.Add($"{entry.Name}: bone[{b}] parent {reparsed.Bones[b].ParentId} != {origTmm.Bones[b].ParentId}");
                    }
                }

                float maxPosDrift = MaxPositionDrift(origData.Vertices, reparsedData.Vertices);
                if (maxPosDrift > 0.01f)
                    failed.Add($"{entry.Name}: max position drift {maxPosDrift:F5} > 0.01");

                if (origTmm.Bones != null && reparsed.Bones != null &&
                    origTmm.Bones.Length == reparsed.Bones.Length)
                {
                    float maxBoneDrift = MaxBoneMatrixDrift(origTmm.Bones, reparsed.Bones);
                    if (maxBoneDrift > 0.01f)
                        failed.Add($"{entry.Name}: max bone parent-space matrix drift {maxBoneDrift:F5} > 0.01");

                    float maxWorldDrift = MaxBoneFlatDrift(origTmm.Bones, reparsed.Bones, b => b.WorldSpaceMatrix);
                    if (maxWorldDrift > 0.01f)
                        failed.Add($"{entry.Name}: max bone world-space matrix drift {maxWorldDrift:F5} > 0.01");

                    float maxIbmDrift = MaxBoneFlatDrift(origTmm.Bones, reparsed.Bones, b => b.InverseBindMatrix);
                    if (maxIbmDrift > 0.01f)
                        failed.Add($"{entry.Name}: max bone inverse-bind matrix drift {maxIbmDrift:F5} > 0.01");

                    float maxColDrift = MaxBoneCollisionDrift(origTmm.Bones, reparsed.Bones);
                    if (maxColDrift > 0.01f)
                        failed.Add($"{entry.Name}: max bone collision (offset/radius) drift {maxColDrift:F5} > 0.01");
                }

                if (origTmm.MeshGroups != null && reparsed.MeshGroups != null &&
                    origTmm.MeshGroups.Length == reparsed.MeshGroups.Length)
                {
                    for (int g = 0; g < origTmm.MeshGroups.Length; g++)
                    {
                        if (origTmm.MeshGroups[g].SubmodelMask != reparsed.MeshGroups[g].SubmodelMask)
                            failed.Add($"{entry.Name}: meshGroup[{g}] submodel_mask {reparsed.MeshGroups[g].SubmodelMask} != {origTmm.MeshGroups[g].SubmodelMask}");
                        if (origTmm.MeshGroups[g].MaterialIndex != reparsed.MeshGroups[g].MaterialIndex)
                            failed.Add($"{entry.Name}: meshGroup[{g}] material_index mismatch");
                    }
                }

                if (origTmm.Attachments != null && reparsed.Attachments != null &&
                    origTmm.Attachments.Length == reparsed.Attachments.Length &&
                    origTmm.Attachments.Length > 0)
                {
                    float maxAdjDrift = MaxAttachmentFlatDrift(origTmm.Attachments, reparsed.Attachments,
                        a => a.AdjustmentTransformMatrix);
                    if (maxAdjDrift > 0.01f)
                        failed.Add($"{entry.Name}: max attachment adjustment-matrix drift {maxAdjDrift:F5} > 0.01");

                    float maxLocalDrift = MaxAttachmentFlatDrift(origTmm.Attachments, reparsed.Attachments,
                        a => a.LocalTransformMatrix);
                    if (maxLocalDrift > 0.01f)
                        failed.Add($"{entry.Name}: max attachment local-matrix drift {maxLocalDrift:F5} > 0.01");
                }

                succeeded++;
            }
            catch (Exception ex)
            {
                failed.Add($"{entry.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            processed++;
        }

        Assert.True(processed > 0, "No TMMs found in ArtModelCacheMeta.bar");
        Assert.True(failed.Count == 0,
            $"Round-trip failures ({failed.Count}/{processed}):\n  " + string.Join("\n  ", failed));
    }

    [SkippableFact]
    public async Task Layer3_VanillaTmaRoundTrip_TrackValuesWithinTolerance()
    {
        Skip.IfNot(GameInstalled,
            "AOM:R game install not found at default Steam path or AOMR_GAME_PATH");

        var modelcacheDir = Path.Combine(GamePath, "modelcache");
        var metaBarPath = Path.Combine(modelcacheDir, "ArtModelCacheMeta.bar");

        var fileIndex = new FileIndex();
        var modelcacheBars = Directory.GetFiles(modelcacheDir, "*.bar", SearchOption.AllDirectories);
        FileIndexBuilder.IndexBarFiles(fileIndex, modelcacheBars);
        var supplemental = FileIndexBuilder.FindSupplementalBarFiles(modelcacheDir);
        FileIndexBuilder.IndexBarFiles(fileIndex, supplemental);

        using var metaStream = File.OpenRead(metaBarPath);
        var metaBar = new BarFile(metaStream);
        metaBar.Load(out _);

        int processed = 0;
        var failed = new List<string>();

        foreach (var entry in metaBar.Entries!)
        {
            if (processed >= 10) break;
            if (!entry.Name.EndsWith(".tmm", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Name.EndsWith(".tmm.data", StringComparison.OrdinalIgnoreCase)) continue;

            // .tmm.data is in a separate model-data BAR; resolve via fileIndex
            var dataIndexEntries = fileIndex.Find(entry.Name + ".data");
            if (dataIndexEntries.Count == 0) continue;

            var tmmStem = Path.GetFileNameWithoutExtension(entry.Name);
            var sourceTmas = await FindTmasForTmm(tmmStem, fileIndex);
            if (sourceTmas.Count == 0) continue;

            try
            {
                using var tmmPooled = entry.ReadDataDecompressedPooled(metaStream);
                if (tmmPooled == null) { processed++; continue; }
                var tmmBytes = tmmPooled.Span.ToArray();

                using var dataBuf = await ReadIndexEntryPooled(dataIndexEntries[0]);
                if (dataBuf == null) { processed++; continue; }
                var dataBytes = dataBuf.Span.ToArray();

                var origTmm = new TmmFile(tmmBytes);
                if (!origTmm.FullyParsed) { processed++; continue; }
                // Animation round-trip only makes sense for rigged models
                if (origTmm.Bones == null || origTmm.Bones.Length == 0) continue;
                var origData = new TmmDataFile(dataBytes, origTmm);
                if (!origData.Parsed) { processed++; continue; }

                var (animName, firstTma) = sourceTmas[0];
                var origTracks = TmaDecoder.DecodeAllTracks(firstTma);
                if (origTracks == null || origTracks.Length == 0) { processed++; continue; }

                var singleAnim = new GlbExporter.GlbAnimation
                {
                    Name = animName,
                    Tracks = origTracks,
                    Duration = firstTma.Duration,
                    FrameCount = firstTma.FrameCount,
                };

                var glbBytes = ConversionHelper.ConvertTmmToGlbBytes(
                    tmmBytes, dataBytes,
                    materials: null,
                    animations: [singleAnim],
                    sourceTmas: [(animName, firstTma)],
                    sourceDdts: null);
                if (glbBytes == null) { failed.Add($"{entry.Name}: ConvertTmmToGlbBytes returned null"); processed++; continue; }

                var model = GlbReader.Parse(glbBytes);
                if (model.Animations == null || model.Animations.Length == 0 || model.Bones == null)
                { failed.Add($"{entry.Name}: GLB has no animations after round-trip"); processed++; continue; }

                var readbackAnim = model.Animations[0];
                GlbExtras.TmaSection? tmaExtras = null;
                model.Extras?.Tma.TryGetValue(readbackAnim.Name, out tmaExtras);

                var (rewrittenBytes, _) = TmaWriter.Write(readbackAnim, model.Bones, tmaExtras);
                var rewrittenTma = new TmaFile(rewrittenBytes);
                if (!rewrittenTma.Parsed) { failed.Add($"{entry.Name}: TmaWriter output failed re-parse"); processed++; continue; }

                var rewrittenTracks = TmaDecoder.DecodeAllTracks(rewrittenTma);
                if (rewrittenTracks == null) { failed.Add($"{entry.Name}: rewritten TMA has no tracks"); processed++; continue; }

                if (rewrittenTma.FrameCount != firstTma.FrameCount)
                {
                    failed.Add($"{entry.Name}: frame count {rewrittenTma.FrameCount} != {firstTma.FrameCount}");
                    processed++;
                    continue;
                }

                var origByName = new Dictionary<string, TmaDecoder.DecodedTrack>(StringComparer.Ordinal);
                foreach (var t in origTracks) origByName[t.Name] = t;

                float maxTDrift = 0f, maxRDrift = 0f;
                foreach (var rt in rewrittenTracks)
                {
                    if (!origByName.TryGetValue(rt.Name, out var ot)) continue;
                    int tFrames = Math.Min(ot.Translations.Length, rt.Translations.Length);
                    for (int f = 0; f < tFrames; f++)
                    {
                        float td = System.Numerics.Vector3.Distance(ot.Translations[f], rt.Translations[f]);
                        if (td > maxTDrift) maxTDrift = td;
                    }
                    int rFrames = Math.Min(ot.Rotations.Length, rt.Rotations.Length);
                    for (int f = 0; f < rFrames; f++)
                    {
                        float rd = QuatMaxComponentDrift(ot.Rotations[f], rt.Rotations[f]);
                        if (rd > maxRDrift) maxRDrift = rd;
                    }
                }

                if (maxTDrift > 0.01f)
                    failed.Add($"{entry.Name}: max translation drift {maxTDrift:F5} > 0.01");
                if (maxRDrift > 1e-3f)
                    failed.Add($"{entry.Name}: max quaternion drift {maxRDrift:F6} > 0.001");
            }
            catch (Exception ex)
            {
                failed.Add($"{entry.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            processed++;
        }

        Assert.True(processed > 0, "No TMMs with paired TMAs found in ArtModelCacheMeta.bar");
        Assert.True(failed.Count == 0,
            $"TMA round-trip failures ({failed.Count}/{processed}):\n  " + string.Join("\n  ", failed));
    }

    // --- Helpers ---

    static float[] Identity16Local() => [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];

    // Replicates the core of BuildGlbAnimations in MainWindow.Export.cs:
    // uses DependencyFinder.FindAnimfileForTmmAsync + AnimationDiscovery + fileIndex.Find.
    static async Task<IReadOnlyList<(string Name, TmaFile Tma)>> FindTmasForTmm(
        string tmmStem, FileIndex fileIndex)
    {
        try
        {
            var animfileEntry = await DependencyFinder.FindAnimfileForTmmAsync(
                tmmStem, fileIndex, ReadIndexEntryPooled);
            if (animfileEntry is not { } animfile) return [];

            var animfileBytes = await ReadIndexEntryPooled(animfile);
            if (animfileBytes == null) return [];
            using (animfileBytes)
            {
                using var dec = BarCompression.EnsureDecompressedPooled(animfileBytes, out _);
                var xmlText = ConversionHelper.GetTextContent(dec.Span, animfile.FileName.ToString());
                var animRefs = AnimationDiscovery.FindAnimationsFromAnimXml(xmlText);
                if (animRefs.Count == 0) return [];

                var result = new List<(string Name, TmaFile Tma)>();
                foreach (var animRef in animRefs)
                {
                    var tmaFileName = Path.GetFileName(animRef.TmaPath.Replace('\\', '/'));
                    if (string.IsNullOrEmpty(tmaFileName)) continue;

                    var tmaEntries = fileIndex.Find(tmaFileName + ".tma");
                    if (tmaEntries.Count == 0) tmaEntries = fileIndex.Find(tmaFileName);
                    if (tmaEntries.Count == 0) continue;

                    var tmaRaw = await ReadIndexEntryPooled(tmaEntries[0]);
                    if (tmaRaw == null) continue;
                    using (tmaRaw)
                    {
                        using var tmaDec = BarCompression.EnsureDecompressedPooled(tmaRaw, out _);
                        var tma = new TmaFile(tmaDec.Memory);
                        if (!tma.Parsed) continue;

                        var baseName = !string.IsNullOrEmpty(animRef.AnimName)
                            ? animRef.AnimName : tmaFileName;
                        result.Add((baseName, tma));
                    }
                }

                // Deduplicate names matching the editor's behaviour
                var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var (n, _) in result) nameCounts[n] = nameCounts.GetValueOrDefault(n) + 1;
                var nameCounters = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < result.Count; i++)
                {
                    var (n, tma) = result[i];
                    if (nameCounts[n] <= 1) continue;
                    int idx = nameCounters.GetValueOrDefault(n) + 1;
                    nameCounters[n] = idx;
                    result[i] = ($"{n} {idx}", tma);
                }

                return result;
            }
        }
        catch
        {
            return [];
        }
    }

    // Opens a BAR, finds the entry by relative path, reads + decompresses into a PooledBuffer.
    // Caller must dispose the returned buffer.
    static ValueTask<PooledBuffer?> ReadIndexEntryPooled(FileIndexEntry entry)
    {
        if (entry.Source != FileIndexSource.BarEntry || entry.BarFilePath == null)
            return ValueTask.FromResult<PooledBuffer?>(null);

        var entryRelPath = entry.EntryRelativePath.ToString();
        if (entryRelPath.Length == 0) return ValueTask.FromResult<PooledBuffer?>(null);

        try
        {
            using var stream = File.OpenRead(entry.BarFilePath);
            var bar = new BarFile(stream);
            if (!bar.Load(out _)) return ValueTask.FromResult<PooledBuffer?>(null);

            BarFileEntry? barEntry = null;
            foreach (var e in bar.Entries!)
            {
                if (string.Equals(e.RelativePath, entryRelPath, StringComparison.OrdinalIgnoreCase))
                { barEntry = e; break; }
            }
            if (barEntry == null) return ValueTask.FromResult<PooledBuffer?>(null);

            var pooled = barEntry.ReadDataDecompressedPooled(stream);
            return ValueTask.FromResult<PooledBuffer?>(pooled);
        }
        catch
        {
            return ValueTask.FromResult<PooledBuffer?>(null);
        }
    }

    static float MaxPositionDrift(TmmVertex[] orig, TmmVertex[] reparsed)
    {
        float max = 0f;
        int count = Math.Min(orig.Length, reparsed.Length);
        for (int i = 0; i < count; i++)
        {
            float dx = Math.Abs((float)orig[i].PosX - (float)reparsed[i].PosX);
            float dy = Math.Abs((float)orig[i].PosY - (float)reparsed[i].PosY);
            float dz = Math.Abs((float)orig[i].PosZ - (float)reparsed[i].PosZ);
            float d = Math.Max(dx, Math.Max(dy, dz));
            if (d > max) max = d;
        }
        return max;
    }

    static float MaxBoneMatrixDrift(TmmBone[] orig, TmmBone[] reparsed)
        => MaxBoneFlatDrift(orig, reparsed, b => b.ParentSpaceMatrix);

    static float MaxBoneFlatDrift(TmmBone[] orig, TmmBone[] reparsed, Func<TmmBone, float[]> selector)
    {
        float max = 0f;
        int count = Math.Min(orig.Length, reparsed.Length);
        for (int b = 0; b < count; b++)
        {
            var a = selector(orig[b]);
            var r = selector(reparsed[b]);
            int len = Math.Min(a.Length, r.Length);
            for (int i = 0; i < len; i++)
            {
                float d = Math.Abs(a[i] - r[i]);
                if (d > max) max = d;
            }
        }
        return max;
    }

    static float MaxBoneCollisionDrift(TmmBone[] orig, TmmBone[] reparsed)
    {
        float max = 0f;
        int count = Math.Min(orig.Length, reparsed.Length);
        for (int b = 0; b < count; b++)
        {
            float dx = Math.Abs(orig[b].CollisionOffsetX - reparsed[b].CollisionOffsetX);
            float dy = Math.Abs(orig[b].CollisionOffsetY - reparsed[b].CollisionOffsetY);
            float dz = Math.Abs(orig[b].CollisionOffsetZ - reparsed[b].CollisionOffsetZ);
            float dr = Math.Abs(orig[b].Radius - reparsed[b].Radius);
            float d = Math.Max(Math.Max(dx, dy), Math.Max(dz, dr));
            if (d > max) max = d;
        }
        return max;
    }

    static float MaxAttachmentFlatDrift(TmmAttachment[] orig, TmmAttachment[] reparsed,
        Func<TmmAttachment, float[]> selector)
    {
        float max = 0f;
        int count = Math.Min(orig.Length, reparsed.Length);
        for (int b = 0; b < count; b++)
        {
            var a = selector(orig[b]);
            var r = selector(reparsed[b]);
            int len = Math.Min(a.Length, r.Length);
            for (int i = 0; i < len; i++)
            {
                float d = Math.Abs(a[i] - r[i]);
                if (d > max) max = d;
            }
        }
        return max;
    }

    // Max per-component absolute drift between two quaternions, accounting for double-cover (q == -q).
    static float QuatMaxComponentDrift(System.Numerics.Quaternion a, System.Numerics.Quaternion b)
    {
        if (a.W < 0f) a = new System.Numerics.Quaternion(-a.X, -a.Y, -a.Z, -a.W);
        if (b.W < 0f) b = new System.Numerics.Quaternion(-b.X, -b.Y, -b.Z, -b.W);
        float dx = Math.Abs(a.X - b.X);
        float dy = Math.Abs(a.Y - b.Y);
        float dz = Math.Abs(a.Z - b.Z);
        float dw = Math.Abs(a.W - b.W);
        return Math.Max(Math.Max(dx, dy), Math.Max(dz, dw));
    }
}
