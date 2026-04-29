using CryBar;
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
    public void Layer2_VanillaTmmRoundTrip_FullPipelineWithEverything()
    {
        string? barPath = Environment.GetEnvironmentVariable("CRYBAR_TEST_DATA_BAR");
        Skip.If(string.IsNullOrEmpty(barPath), "CRYBAR_TEST_DATA_BAR not configured.");
        Skip.IfNot(System.IO.File.Exists(barPath), $"CRYBAR_TEST_DATA_BAR points to missing file: {barPath}");

        using var stream = System.IO.File.OpenRead(barPath);
        var bar = new BarFile(stream);
        var loaded = bar.Load(out var loadError);
        Assert.True(loaded, $"Failed to load BAR: {loadError}");

        // Pre-build name lookup for fast TMA discovery
        var barEntryIndex = BuildBarNameIndex(bar.Entries!);

        int processed = 0;
        var failed = new List<string>();
        foreach (var entry in bar.Entries!)
        {
            if (processed >= 20) break;
            if (!entry.Name.EndsWith(".tmm", StringComparison.OrdinalIgnoreCase)) continue;

            var dataEntry = FindEntry(barEntryIndex, entry.Name + ".data");
            if (dataEntry == null) continue;

            try
            {
                var tmmBytes = entry.ReadDataRaw(stream);
                var dataBytes = dataEntry.ReadDataRaw(stream);

                var origTmm = new TmmFile(tmmBytes);
                if (!origTmm.FullyParsed) continue;
                var origData = new TmmDataFile(dataBytes, origTmm);
                if (!origData.Parsed || origData.Vertices == null) continue;

                // Find TMAs by stem pattern: <tmm_basename>_*.tma
                var stem = System.IO.Path.GetFileNameWithoutExtension(entry.Name);
                var (sourceTmas, glbAnimations) = FindTmasForStem(stem, barEntryIndex, stream);

                // DDT lookup omitted: material XML lookup requires a full file index.
                // Pass empty lists so extras.Ddt is populated with no entries.
                var sourceDdts = new List<(string Material, DDTImage Ddt)>();
                List<GlbExporter.GlbMaterial>? glbMaterials = null;

                var glbBytes = ConversionHelper.ConvertTmmToGlbBytes(
                    tmmBytes, dataBytes, glbMaterials, glbAnimations,
                    sourceTmas: sourceTmas, sourceDdts: sourceDdts);
                if (glbBytes == null) { failed.Add($"{entry.Name}: ConvertTmmToGlbBytes returned null"); continue; }

                var model = GlbReader.Parse(glbBytes);
                var (newTmmBytes, newDataBytes, _) = TmmWriter.Write(model);

                var reparsed = new TmmFile(newTmmBytes);
                if (!reparsed.FullyParsed) { failed.Add($"{entry.Name}: re-parse failed"); continue; }

                var reparsedData = new TmmDataFile(newDataBytes, reparsed);
                if (!reparsedData.Parsed || reparsedData.Vertices == null)
                { failed.Add($"{entry.Name}: re-parse data failed"); continue; }

                // Count assertions
                if (reparsed.NumVertices != origTmm.NumVertices)
                    failed.Add($"{entry.Name}: vert count {reparsed.NumVertices} != {origTmm.NumVertices}");
                if (reparsed.NumTriangleVerts != origTmm.NumTriangleVerts)
                    failed.Add($"{entry.Name}: idx count {reparsed.NumTriangleVerts} != {origTmm.NumTriangleVerts}");
                if ((reparsed.Bones?.Length ?? 0) != (origTmm.Bones?.Length ?? 0))
                    failed.Add($"{entry.Name}: bone count {reparsed.Bones?.Length ?? 0} != {origTmm.Bones?.Length ?? 0}");
                if ((reparsed.Attachments?.Length ?? 0) != (origTmm.Attachments?.Length ?? 0))
                    failed.Add($"{entry.Name}: attachment count mismatch");
                if ((reparsed.Materials?.Length ?? 0) != (origTmm.Materials?.Length ?? 0))
                    failed.Add($"{entry.Name}: material count {reparsed.Materials?.Length ?? 0} != {origTmm.Materials?.Length ?? 0}");

                // Bone name + parent checks
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

                // Vertex position drift: Half-precision gives ~3 decimal digits, so 0.01 units is safe
                float maxPosDrift = MaxPositionDrift(origData.Vertices, reparsedData.Vertices);
                if (maxPosDrift > 0.01f)
                    failed.Add($"{entry.Name}: max position drift {maxPosDrift:F5} exceeds 0.01");

                // Bone parent-space matrix drift (same tolerance — values stored as float32)
                if (origTmm.Bones != null && reparsed.Bones != null &&
                    origTmm.Bones.Length == reparsed.Bones.Length)
                {
                    float maxBoneDrift = MaxBoneMatrixDrift(origTmm.Bones, reparsed.Bones);
                    if (maxBoneDrift > 0.01f)
                        failed.Add($"{entry.Name}: max bone matrix drift {maxBoneDrift:F5} exceeds 0.01");
                }

                // TMA round-trip for each animation embedded in the GLB
                if (model.Animations != null && model.Bones != null)
                {
                    foreach (var anim in model.Animations)
                    {
                        try
                        {
                            GlbExtras.TmaSection? tmaExtras = null;
                            model.Extras?.Tma.TryGetValue(anim.Name, out tmaExtras);
                            var (tmaBytes2, _) = TmaWriter.Write(anim, model.Bones, tmaExtras);
                            var revalidate = new TmaFile(tmaBytes2);
                            if (!revalidate.Parsed)
                                failed.Add($"{entry.Name}: TMA re-parse failed for anim '{anim.Name}'");
                        }
                        catch (Exception ex)
                        {
                            failed.Add($"{entry.Name}: TMA write '{anim.Name}': {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
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
    public void Layer3_VanillaTmaRoundTrip_TrackValuesWithinTolerance()
    {
        string? barPath = Environment.GetEnvironmentVariable("CRYBAR_TEST_DATA_BAR");
        Skip.If(string.IsNullOrEmpty(barPath), "CRYBAR_TEST_DATA_BAR not configured.");
        Skip.IfNot(System.IO.File.Exists(barPath), $"CRYBAR_TEST_DATA_BAR points to missing file: {barPath}");

        using var stream = System.IO.File.OpenRead(barPath);
        var bar = new BarFile(stream);
        var loaded = bar.Load(out var loadError);
        Assert.True(loaded, $"Failed to load BAR: {loadError}");

        var barEntryIndex = BuildBarNameIndex(bar.Entries!);

        int processed = 0;
        var failed = new List<string>();
        foreach (var entry in bar.Entries!)
        {
            if (processed >= 20) break;
            if (!entry.Name.EndsWith(".tma", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var tmaBytes = entry.ReadDataRaw(stream);
                var origTma = new TmaFile(tmaBytes);
                if (!origTma.Parsed) continue;

                var origTracks = TmaDecoder.DecodeAllTracks(origTma);
                if (origTracks == null || origTracks.Length == 0) continue;

                // Find matching TMM: strip trailing _<word> segments to find stem
                // e.g. "gargarensis_idle.tma" -> look for "gargarensis.tmm"
                var tmaStem = System.IO.Path.GetFileNameWithoutExtension(entry.Name);
                var tmmEntry = FindTmmForTma(tmaStem, barEntryIndex);
                if (tmmEntry == null) continue;

                var tmmDataEntry = FindEntry(barEntryIndex, tmmEntry.Name + ".data");
                if (tmmDataEntry == null) continue;

                var tmmBytesArr = tmmEntry.ReadDataRaw(stream);
                var tmmDataBytesArr = tmmDataEntry.ReadDataRaw(stream);

                var tmm = new TmmFile(tmmBytesArr);
                if (!tmm.FullyParsed) continue;
                var tmmData = new TmmDataFile(tmmDataBytesArr, tmm);
                if (!tmmData.Parsed) continue;

                // Derive animation name from TMA filename
                var animName = tmaStem;

                // Build single-animation GLB through the same path the editor uses
                var singleTmaList = new List<(string Name, TmaFile Tma)> { (animName, origTma) };
                var glbAnim = new GlbExporter.GlbAnimation
                {
                    Name = animName,
                    Tracks = origTracks,
                    Duration = origTma.Duration,
                    FrameCount = origTma.FrameCount,
                };
                var glbAnims = new List<GlbExporter.GlbAnimation> { glbAnim };

                var glbBytes = ConversionHelper.ConvertTmmToGlbBytes(
                    tmmBytesArr, tmmDataBytesArr,
                    materials: null, animations: glbAnims,
                    sourceTmas: singleTmaList, sourceDdts: null);
                if (glbBytes == null) continue;

                var model = GlbReader.Parse(glbBytes);
                if (model.Animations == null || model.Animations.Length == 0 || model.Bones == null) continue;

                var readbackAnim = model.Animations[0];

                // Round-trip through TmaWriter
                GlbExtras.TmaSection? tmaExtras = null;
                model.Extras?.Tma.TryGetValue(readbackAnim.Name, out tmaExtras);
                var (rewrittenBytes, _) = TmaWriter.Write(readbackAnim, model.Bones, tmaExtras);

                var rewrittenTma = new TmaFile(rewrittenBytes);
                if (!rewrittenTma.Parsed) { failed.Add($"{entry.Name}: TmaWriter output failed re-parse"); continue; }

                var rewrittenTracks = TmaDecoder.DecodeAllTracks(rewrittenTma);
                if (rewrittenTracks == null) { failed.Add($"{entry.Name}: rewritten TMA has no tracks"); continue; }

                // Frame count must match
                if (rewrittenTma.FrameCount != origTma.FrameCount)
                {
                    failed.Add($"{entry.Name}: frame count {rewrittenTma.FrameCount} != {origTma.FrameCount}");
                    processed++;
                    continue;
                }

                // Compare decoded-original vs decoded-rewritten per track
                // Tolerance: translation 0.01 game units; rotation 1e-3 per quaternion component
                // (Quat64 quantization is ~5e-5; 1e-3 allows for bind-pose composition noise)
                var tracksByName = new Dictionary<string, TmaDecoder.DecodedTrack>(StringComparer.Ordinal);
                foreach (var t in origTracks) tracksByName[t.Name] = t;

                float maxTDrift = 0f, maxRDrift = 0f;
                foreach (var rt in rewrittenTracks)
                {
                    if (!tracksByName.TryGetValue(rt.Name, out var ot)) continue;
                    int frames = Math.Min(ot.Translations.Length, rt.Translations.Length);
                    for (int f = 0; f < frames; f++)
                    {
                        float td = System.Numerics.Vector3.Distance(ot.Translations[f], rt.Translations[f]);
                        if (td > maxTDrift) maxTDrift = td;
                    }
                    frames = Math.Min(ot.Rotations.Length, rt.Rotations.Length);
                    for (int f = 0; f < frames; f++)
                    {
                        float rd = QuatMaxComponentDrift(ot.Rotations[f], rt.Rotations[f]);
                        if (rd > maxRDrift) maxRDrift = rd;
                    }
                }

                if (maxTDrift > 0.01f)
                    failed.Add($"{entry.Name}: max translation drift {maxTDrift:F5} exceeds 0.01");
                if (maxRDrift > 1e-3f)
                    failed.Add($"{entry.Name}: max quaternion drift {maxRDrift:F6} exceeds 0.001");
            }
            catch (Exception ex)
            {
                failed.Add($"{entry.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            processed++;
        }

        Assert.True(processed > 0, "No TMAs processed - check BAR contents");
        Assert.True(failed.Count == 0, $"TMA round-trip failures ({failed.Count}/{processed}):\n  " + string.Join("\n  ", failed));
    }

    static float[] Identity16Local() => [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];

    // Build case-insensitive name -> entry lookup for O(1) lookups during iteration.
    static Dictionary<string, BarFileEntry> BuildBarNameIndex(IReadOnlyList<BarFileEntry> entries)
    {
        var index = new Dictionary<string, BarFileEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            index.TryAdd(e.Name, e);
        return index;
    }

    static BarFileEntry? FindEntry(Dictionary<string, BarFileEntry> index, string name)
        => index.TryGetValue(name, out var e) ? e : null;

    // Finds TMAs whose names start with "<stem>_" (e.g. "gargarensis_idle.tma" for stem "gargarensis").
    // Returns the decoded tracks and the GlbExporter.GlbAnimation list ready to pass to ConvertTmmToGlbBytes.
    static (List<(string Name, TmaFile Tma)> sourceTmas, List<GlbExporter.GlbAnimation>? glbAnimations)
        FindTmasForStem(string stem, Dictionary<string, BarFileEntry> index, System.IO.FileStream stream)
    {
        var prefix = stem + "_";
        var sourceTmas = new List<(string Name, TmaFile Tma)>();
        var glbAnimations = new List<GlbExporter.GlbAnimation>();

        foreach (var kv in index)
        {
            if (!kv.Key.EndsWith(".tma", StringComparison.OrdinalIgnoreCase)) continue;
            if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var tmaBytes = kv.Value.ReadDataRaw(stream);
                var tma = new TmaFile(tmaBytes);
                if (!tma.Parsed) continue;

                var tracks = TmaDecoder.DecodeAllTracks(tma);
                if (tracks == null || tracks.Length == 0) continue;

                // Animation name: stem without .tma, e.g. "gargarensis_idle"
                var animName = System.IO.Path.GetFileNameWithoutExtension(kv.Key);
                sourceTmas.Add((animName, tma));
                glbAnimations.Add(new GlbExporter.GlbAnimation
                {
                    Name = animName,
                    Tracks = tracks,
                    Duration = tma.Duration,
                    FrameCount = tma.FrameCount,
                });
            }
            catch { /* best-effort; skip broken TMAs */ }
        }

        return (sourceTmas, glbAnimations.Count > 0 ? glbAnimations : null);
    }

    // Finds the TMM for a TMA by stripping trailing "_<word>" segments from the TMA stem.
    // e.g. "gargarensis_idle" -> try "gargarensis.tmm".
    // Tries progressively shorter stems until a match is found or no underscore remains.
    static BarFileEntry? FindTmmForTma(string tmaStem, Dictionary<string, BarFileEntry> index)
    {
        var stem = tmaStem;
        while (true)
        {
            int us = stem.LastIndexOf('_');
            if (us < 0) break;
            stem = stem[..us];
            var candidate = stem + ".tmm";
            if (index.TryGetValue(candidate, out var entry)) return entry;
        }
        return null;
    }

    // Max element-wise drift across all vertices for XYZ positions.
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

    // Max element-wise drift across all bone parent-space matrices.
    static float MaxBoneMatrixDrift(TmmBone[] orig, TmmBone[] reparsed)
    {
        float max = 0f;
        int count = Math.Min(orig.Length, reparsed.Length);
        for (int b = 0; b < count; b++)
        {
            var a = orig[b].ParentSpaceMatrix;
            var r = reparsed[b].ParentSpaceMatrix;
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
        // Canonical form: ensure W >= 0 (flip sign if needed)
        if (a.W < 0f) a = new System.Numerics.Quaternion(-a.X, -a.Y, -a.Z, -a.W);
        if (b.W < 0f) b = new System.Numerics.Quaternion(-b.X, -b.Y, -b.Z, -b.W);
        float dx = Math.Abs(a.X - b.X);
        float dy = Math.Abs(a.Y - b.Y);
        float dz = Math.Abs(a.Z - b.Z);
        float dw = Math.Abs(a.W - b.W);
        return Math.Max(Math.Max(dx, dy), Math.Max(dz, dw));
    }
}
