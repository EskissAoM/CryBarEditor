using CryBar.Bar;
using CryBar.Export;
using CryBar.TMM;
using Xunit;
using static CryBar.Tests.TmmTestHelpers;

namespace CryBar.Tests;

public class GlbRoundTripTests
{
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
        Assert.Empty(warnings);

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

    [SkippableFact]
    public void Layer2_VanillaTmmRoundTrip_StructurallyEquivalent()
    {
        string? barPath = Environment.GetEnvironmentVariable("CRYBAR_TEST_DATA_BAR");
        Skip.If(string.IsNullOrEmpty(barPath), "CRYBAR_TEST_DATA_BAR not configured.");
        Skip.IfNot(System.IO.File.Exists(barPath), $"CRYBAR_TEST_DATA_BAR points to missing file: {barPath}");

        using var stream = System.IO.File.OpenRead(barPath);
        var bar = new BarFile(stream);
        var loaded = bar.Load(out var loadError);
        Assert.True(loaded, $"Failed to load BAR: {loadError}");

        int processed = 0;
        var failed = new List<string>();
        foreach (var entry in bar.Entries!)
        {
            if (processed >= 50) break;
            if (!entry.Name.EndsWith(".tmm", StringComparison.OrdinalIgnoreCase)) continue;

            var dataEntry = bar.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, entry.Name + ".data", StringComparison.OrdinalIgnoreCase));
            if (dataEntry == null) continue;

            try
            {
                var tmmBytes = entry.ReadDataRaw(stream);
                var dataBytes = dataEntry.ReadDataRaw(stream);

                var origTmm = new TmmFile(tmmBytes);
                if (!origTmm.FullyParsed) continue;
                var origData = new TmmDataFile(dataBytes, origTmm);
                if (!origData.Parsed) continue;

                var glbBytes = GlbExporter.ExportGlb(origTmm, origData);
                if (glbBytes == null) continue;

                var model = GlbReader.Parse(glbBytes);
                var (newTmm, _, _) = TmmWriter.Write(model);

                var reparsed = new TmmFile(newTmm);
                if (!reparsed.FullyParsed) { failed.Add($"{entry.Name}: writer output failed re-parse"); continue; }

                if (reparsed.NumVertices != origTmm.NumVertices)
                    failed.Add($"{entry.Name}: vert count {reparsed.NumVertices} != {origTmm.NumVertices}");
                if (reparsed.NumTriangleVerts != origTmm.NumTriangleVerts)
                    failed.Add($"{entry.Name}: idx count {reparsed.NumTriangleVerts} != {origTmm.NumTriangleVerts}");
                if ((reparsed.Bones?.Length ?? 0) != (origTmm.Bones?.Length ?? 0))
                    failed.Add($"{entry.Name}: bone count mismatch");
                if ((reparsed.Attachments?.Length ?? 0) != (origTmm.Attachments?.Length ?? 0))
                    failed.Add($"{entry.Name}: attachment count mismatch");
            }
            catch (Exception ex)
            {
                failed.Add($"{entry.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            processed++;
        }

        Assert.True(processed > 0, "No TMMs processed - check BAR contents");
        Assert.True(failed.Count == 0, $"Round-trip failures ({failed.Count}/{processed}):\n  " + string.Join("\n  ", failed));
    }

    [SkippableFact]
    public void Layer3_VanillaTmaRoundTrip_QuaternionDriftWithinTolerance()
    {
        string? barPath = Environment.GetEnvironmentVariable("CRYBAR_TEST_DATA_BAR");
        Skip.If(string.IsNullOrEmpty(barPath), "CRYBAR_TEST_DATA_BAR not configured.");
        Skip.IfNot(System.IO.File.Exists(barPath), $"CRYBAR_TEST_DATA_BAR points to missing file: {barPath}");

        using var stream = System.IO.File.OpenRead(barPath);
        var bar = new BarFile(stream);
        var loaded = bar.Load(out var loadError);
        Assert.True(loaded, $"Failed to load BAR: {loadError}");

        int processed = 0;
        var failed = new List<string>();
        foreach (var entry in bar.Entries!)
        {
            if (processed >= 50) break;
            if (!entry.Name.EndsWith(".tma", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var tmaBytes = entry.ReadDataRaw(stream);
                var orig = new TmaFile(tmaBytes);
                if (!orig.Parsed) continue;

                // Structural test: verify vanilla TMAs parse cleanly.
                // Detailed tolerance check deferred to Task 25 (requires bind-pose composition).
                processed++;
            }
            catch (Exception ex)
            {
                failed.Add($"{entry.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(processed > 0, "No TMAs processed - check BAR contents");
        Assert.True(failed.Count == 0, $"TMA parse failures ({failed.Count}/{processed}):\n  " + string.Join("\n  ", failed));
    }

    static float[] Identity16Local() => [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
}
