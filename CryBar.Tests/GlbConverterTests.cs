using CryBar.Export;
using static CryBar.Tests.TmmTestHelpers;

namespace CryBar.Tests;

public class GlbConverterTests
{
    static GlbModel BuildMinimalUntexturedModel()
    {
        return new GlbModel
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
    }

    [Fact]
    public async Task ConvertAsync_NoAnimsNoTextures_ProducesTmmAndTmmDataOnly()
    {
        var model = BuildMinimalUntexturedModel();
        var glbName = "test_model";

        var result = await GlbConverter.ConvertAsync(model, glbName,
            new Dictionary<string, GlbConverter.DdtMaterialParams>());

        Assert.Equal(2, result.Files.Count);
        Assert.Contains(result.Files, f => f.Name == "test_model.tmm");
        Assert.Contains(result.Files, f => f.Name == "test_model.tmm.data");
    }

    [Fact]
    public void Inspect_NoAnimsNoTextures_ListsTmmAndTmmDataOnly()
    {
        var model = BuildMinimalUntexturedModel();

        var inspection = GlbConverter.Inspect(model, "test_model");

        Assert.Equal(2, inspection.PlannedFiles.Count);
        Assert.Contains(inspection.PlannedFiles, f => f.Name == "test_model.tmm");
        Assert.Contains(inspection.PlannedFiles, f => f.Name == "test_model.tmm.data");
        Assert.Empty(inspection.MaterialsNeedingDdtParams);
    }

    [Fact]
    public async Task ConvertAsync_TwoAnimationsOnSkinnedModel_ProducesTwoTmaFiles()
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
                new GlbBone { Name = "root", ParentIndex = -1,
                    LocalMatrix = Identity16Local(), InverseBindMatrix = Identity16Local() }
            ],
            Animations =
            [
                new GlbAnimation { Name = "idle", Duration = 1f, FrameCount = 2, Tracks = [] },
                new GlbAnimation { Name = "walk", Duration = 1f, FrameCount = 2, Tracks = [] },
            ],
        };

        var result = await GlbConverter.ConvertAsync(model, "char",
            new Dictionary<string, GlbConverter.DdtMaterialParams>());

        Assert.Contains(result.Files, f => f.Name == "idle.tma");
        Assert.Contains(result.Files, f => f.Name == "walk.tma");
    }

    [Fact]
    public void Inspect_TwoAnimations_ListsTmaFiles()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Materials = [],
            Bones = [new GlbBone { Name = "root", ParentIndex = -1,
                LocalMatrix = Identity16Local(), InverseBindMatrix = Identity16Local() }],
            Animations =
            [
                new GlbAnimation { Name = "idle", Duration = 1f, FrameCount = 2, Tracks = [] },
                new GlbAnimation { Name = "walk", Duration = 1f, FrameCount = 2, Tracks = [] },
            ],
        };

        var inspection = GlbConverter.Inspect(model, "char");

        Assert.Contains(inspection.PlannedFiles, f => f.Name == "idle.tma");
        Assert.Contains(inspection.PlannedFiles, f => f.Name == "walk.tma");
    }

    [Fact]
    public async Task ConvertAsync_MaterialWithBaseColorAndParams_ProducesDdtFile()
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(4, 4);
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        var pngBytes = ms.ToArray();

        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "body",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1],
                        Indices = [0, 1, 2],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "body", BaseColorPng = pngBytes }],
        };

        var ddtParams = new Dictionary<string, GlbConverter.DdtMaterialParams>
        {
            ["body"] = new GlbConverter.DdtMaterialParams(
                DDTVersion.RTS4, DDTUsage.None, DDTAlpha.None, DDTFormat.DXT1, 1, null),
        };

        var result = await GlbConverter.ConvertAsync(model, "char", ddtParams);

        Assert.Contains(result.Files, f => f.Name == "body.ddt");
        var ddtFile = result.Files.First(f => f.Name == "body.ddt");
        Assert.True(ddtFile.Bytes.Length > 0);
    }

    [Fact]
    public void Inspect_MaterialWithBaseColorAndExtras_DoesNotFlagAsMissingParams()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Materials = [new GlbMaterial { Name = "body", BaseColorPng = [1, 2, 3] }],
            Extras = new GlbExtras
            {
                Ddt = { new GlbExtras.DdtEntry { Material = "body" } },
            },
        };

        var inspection = GlbConverter.Inspect(model, "char");

        Assert.Empty(inspection.MaterialsNeedingDdtParams);
        Assert.Contains(inspection.PlannedFiles, f => f.Name == "body.ddt" && !f.NeedsDdtParams);
    }

    [Fact]
    public void Inspect_MaterialWithBaseColorAndNoExtras_FlagsAsMissingParams()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Materials = [new GlbMaterial { Name = "body", BaseColorPng = [1, 2, 3] }],
        };

        var inspection = GlbConverter.Inspect(model, "char");

        Assert.Single(inspection.MaterialsNeedingDdtParams);
        Assert.Contains("body", inspection.MaterialsNeedingDdtParams);
        Assert.Contains(inspection.PlannedFiles,
            f => f.Name == "body.ddt" && f.NeedsDdtParams && f.DdtMaterialName == "body");
    }

    [Fact]
    public async Task ConvertAsync_MaterialWithPngButNoParams_EmitsWarningAndSkipsDdt()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Materials = [new GlbMaterial { Name = "cloth", BaseColorPng = [1, 2, 3, 4] }],
        };

        var result = await GlbConverter.ConvertAsync(model, "char",
            new Dictionary<string, GlbConverter.DdtMaterialParams>());

        Assert.DoesNotContain(result.Files, f => f.Name == "cloth.ddt");
        Assert.Contains(result.Warnings, w =>
            w.Contains("cloth", StringComparison.Ordinal) &&
            w.Contains("DDT", StringComparison.Ordinal) &&
            w.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConvertAsync_ExtrasCarryDdtParams_NoUserParams_ProducesDdtFile()
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(4, 4);
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        var pngBytes = ms.ToArray();

        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "body",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1],
                        Indices = [0, 1, 2],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "body", BaseColorPng = pngBytes }],
            Extras = new GlbExtras
            {
                Ddt =
                {
                    new GlbExtras.DdtEntry
                    {
                        Material = "body",
                        Version = DDTVersion.RTS4,
                        Usage = DDTUsage.None,
                        Alpha = DDTAlpha.None,
                        Format = DDTFormat.DXT1,
                        MipLevels = 1,
                    },
                },
            },
        };

        var result = await GlbConverter.ConvertAsync(model, "char",
            new Dictionary<string, GlbConverter.DdtMaterialParams>());

        Assert.Contains(result.Files, f => f.Name == "body.ddt");
        Assert.DoesNotContain(result.Warnings, w =>
            w.Contains("body", StringComparison.Ordinal) &&
            w.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConvertAsync_MaterialWithMask1AndParams_ProducesMasks1Ddt()
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(4, 4);
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        var pngBytes = ms.ToArray();

        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives = [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "body",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0],
                        Normals   = [0, 0, 1,  0, 0, 1,  0, 0, 1],
                        Tangents  = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1],
                        Indices   = [0, 1, 2],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "body", Mask1Png = pngBytes, Mask2Png = pngBytes }],
        };

        var ddtParams = new Dictionary<string, GlbConverter.DdtMaterialParams>
        {
            ["body_masks1"] = new GlbConverter.DdtMaterialParams(
                DDTVersion.RTS4, DDTUsage.None, DDTAlpha.None, DDTFormat.DXT1, 1, null),
            ["body_masks2"] = new GlbConverter.DdtMaterialParams(
                DDTVersion.RTS4, DDTUsage.None, DDTAlpha.None, DDTFormat.DXT1, 1, null),
        };

        var result = await GlbConverter.ConvertAsync(model, "char", ddtParams);

        Assert.Contains(result.Files, f => f.Name == "body_masks1.ddt");
        Assert.Contains(result.Files, f => f.Name == "body_masks2.ddt");
    }

    [Fact]
    public void Inspect_MaterialWithMasksAndNoExtras_FlagsMissingForBoth()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Materials = [
                new GlbMaterial { Name = "body", Mask1Png = [1, 2, 3], Mask2Png = [4, 5, 6] }
            ],
        };

        var inspection = GlbConverter.Inspect(model, "char");

        Assert.Equal(2, inspection.MaterialsNeedingDdtParams.Count);
        Assert.Contains("body_masks1", inspection.MaterialsNeedingDdtParams);
        Assert.Contains("body_masks2", inspection.MaterialsNeedingDdtParams);
        Assert.Contains(inspection.PlannedFiles,
            f => f.Name == "body_masks1.ddt" && f.NeedsDdtParams && f.DdtMaterialName == "body_masks1");
        Assert.Contains(inspection.PlannedFiles,
            f => f.Name == "body_masks2.ddt" && f.NeedsDdtParams && f.DdtMaterialName == "body_masks2");
    }

    [Fact]
    public void Inspect_MaterialWithMasksAndExtras_DoesNotFlagAsMissing()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Materials = [
                new GlbMaterial { Name = "body", Mask1Png = [1, 2, 3], Mask2Png = [4, 5, 6] }
            ],
            Extras = new GlbExtras
            {
                Ddt =
                {
                    new GlbExtras.DdtEntry { Material = "body_masks1" },
                    new GlbExtras.DdtEntry { Material = "body_masks2" },
                },
            },
        };

        var inspection = GlbConverter.Inspect(model, "char");

        Assert.Empty(inspection.MaterialsNeedingDdtParams);
        Assert.Contains(inspection.PlannedFiles, f => f.Name == "body_masks1.ddt" && !f.NeedsDdtParams);
        Assert.Contains(inspection.PlannedFiles, f => f.Name == "body_masks2.ddt" && !f.NeedsDdtParams);
    }

    [Fact]
    public async Task ConvertAsync_MaterialWithMask1ButNoParams_EmitsWarningAndSkipsDdt()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
            Materials = [new GlbMaterial { Name = "wall", Mask1Png = [1, 2, 3, 4] }],
        };

        var result = await GlbConverter.ConvertAsync(model, "char",
            new Dictionary<string, GlbConverter.DdtMaterialParams>());

        Assert.DoesNotContain(result.Files, f => f.Name == "wall_masks1.ddt");
        Assert.Contains(result.Warnings, w =>
            w.Contains("wall", StringComparison.Ordinal) &&
            w.Contains("DDT", StringComparison.Ordinal) &&
            w.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }
}
