using CommunityToolkit.HighPerformance;

using CryBar;
using CryBar.BCnEncoder.Shared;

namespace CryBar.Tests;

/// <summary>
/// Asserts the SIMD bilinear resampler produces byte-identical output to the
/// scalar reference. Failing here means a SIMD code path could silently change
/// terrain texture pixels in scenario previews and exported GLB materials.
/// </summary>
public class BilinearSimdParityTests
{
    static Memory2D<ColorRgba32> MakeSource(int w, int h, int seed)
    {
        var rng = new Random(seed);
        var buf = new ColorRgba32[w * h];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = new ColorRgba32(
                (byte)rng.Next(256),
                (byte)rng.Next(256),
                (byte)rng.Next(256),
                (byte)rng.Next(256));
        return buf.AsMemory().AsMemory2D(h, w);
    }

    [Theory]
    [InlineData(1024, 1024, 256, 256, 1)]   // typical terrain downsample
    [InlineData(512, 512, 256, 256, 2)]
    [InlineData(256, 256, 128, 128, 3)]
    [InlineData(1024, 512, 256, 256, 4)]    // non-square source
    [InlineData(512, 1024, 256, 256, 5)]
    [InlineData(64, 64, 256, 256, 6)]       // upsample
    [InlineData(2, 2, 8, 8, 7)]             // tiny upsample (edge clamp dominates)
    [InlineData(8, 8, 4, 4, 8)]
    [InlineData(1, 1, 4, 4, 9)]             // 1x1 source - all pixels clamp to (0,0)
    [InlineData(7, 5, 13, 11, 10)]          // odd sizes
    [InlineData(257, 129, 256, 128, 11)]    // off-by-one strides
    public void Simd_MatchesScalar_ByteForByte(int srcW, int srcH, int dstW, int dstH, int seed)
    {
        var src = MakeSource(srcW, srcH, seed);
        var dstScalar = new byte[dstW * dstH * 4];
        var dstSimd = new byte[dstW * dstH * 4];

        DDTImage.ResampleBilinearRgba8Scalar(src, dstScalar, dstW, dstH);
        DDTImage.ResampleBilinearRgba8Simd(src, dstSimd, dstW, dstH);

        Assert.Equal(dstScalar, dstSimd);
    }

    [Fact]
    public void Simd_GradientSource_MatchesScalar()
    {
        // Smooth gradient where bilinear filtering really exercises blend math.
        const int W = 256, H = 256;
        var buf = new ColorRgba32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                buf[y * W + x] = new ColorRgba32((byte)x, (byte)y, (byte)((x + y) >> 1), 255);
        var src = buf.AsMemory().AsMemory2D(H, W);

        var dstScalar = new byte[64 * 64 * 4];
        var dstSimd = new byte[64 * 64 * 4];

        DDTImage.ResampleBilinearRgba8Scalar(src, dstScalar, 64, 64);
        DDTImage.ResampleBilinearRgba8Simd(src, dstSimd, 64, 64);

        Assert.Equal(dstScalar, dstSimd);
    }

    [Fact]
    public void Simd_AllZeroSource_ProducesZeros()
    {
        var src = new ColorRgba32[64 * 64].AsMemory().AsMemory2D(64, 64);
        var dst = new byte[32 * 32 * 4];
        DDTImage.ResampleBilinearRgba8Simd(src, dst, 32, 32);
        Assert.All(dst, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Simd_AllMaxSource_ProducesMax()
    {
        var buf = new ColorRgba32[64 * 64];
        for (int i = 0; i < buf.Length; i++) buf[i] = new ColorRgba32(255, 255, 255, 255);
        var src = buf.AsMemory().AsMemory2D(64, 64);
        var dst = new byte[32 * 32 * 4];
        DDTImage.ResampleBilinearRgba8Simd(src, dst, 32, 32);
        Assert.All(dst, b => Assert.Equal(255, b));
    }
}
