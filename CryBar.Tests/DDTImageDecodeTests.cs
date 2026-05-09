using CryBar;
using CryBar.Bar;

namespace CryBar.Tests;

[Collection("Integration")]
public class DDTImageDecodeTests
{
    static string GamePath =>
        Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";

    [SkippableFact]
    public async Task DecodeBaseColorOnlyAsync_RealTerrainDDT_Returns256x256RGBA8()
    {
        Skip.IfNot(Directory.Exists(GamePath), "Game install not found");

        var barPath = Path.Combine(GamePath, "art", "ArtTerrainTextures.bar");
        Skip.IfNot(File.Exists(barPath), "ArtTerrainTextures.bar not found");

        using var stream = File.OpenRead(barPath);
        var bar = new BarFile(stream);
        Assert.True(bar.Load(out _));

        var entry = bar.Entries!.FirstOrDefault(e =>
            e.Name.EndsWith("_basecolor.ddt", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);

        using var pooled = entry!.ReadDataDecompressedPooled(stream);
        Assert.NotNull(pooled);

        var rgba = await DDTImage.DecodeBaseColorOnlyAsync(pooled!.Memory, 256);

        Assert.NotNull(rgba);
        Assert.Equal(256 * 256 * 4, rgba!.Length);
    }
}
