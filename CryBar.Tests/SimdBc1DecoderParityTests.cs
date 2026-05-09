using System.Runtime.InteropServices;

using CryBar;
using CryBar.BCnEncoder.Shared;

namespace CryBar.Tests;

/// <summary>
/// Asserts the SIMD BC1 decoder produces byte-identical output to the vendored
/// Bc1Block.Decode reference. BC1 is the dominant DDT format (most RTS4 textures),
/// so any divergence here cascades into preview pixels and any decoded texture
/// baked into exported GLB materials.
/// </summary>
public class SimdBc1DecoderParityTests
{
    static Bc1Block MakeBlock(ushort c0, ushort c1, uint indices) => new Bc1Block
    {
        color0 = new ColorRgb565 { data = c0 },
        color1 = new ColorRgb565 { data = c1 },
        colorIndices = indices
    };

    static byte[] BlockToBytes(Bc1Block block)
    {
        var span = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref block, 1));
        return span.ToArray();
    }

    static void AssertParity(Bc1Block block, bool useAlpha)
    {
        var vendored = block.Decode(useAlpha);
        var vendoredBytes = MemoryMarshal.AsBytes(vendored.AsSpan).ToArray();

        var blockBytes = BlockToBytes(block);
        var simdOut = new byte[64];
        SimdBc1Decoder.DecodeBlock(blockBytes, simdOut, useAlpha);

        Assert.Equal(vendoredBytes, simdOut);
    }

    // c0 > c1 -> 4-color palette mode: [c0, c1, third1, third2]. useAlpha is a no-op here.
    [Theory]
    [InlineData(0xFFFFu, 0x0000u, 0xE4E4E4E4u)] // indices spanning 0,1,2,3
    [InlineData(0xF800u, 0x001Fu, 0x00000000u)] // pure red vs pure blue, all index 0
    [InlineData(0xF800u, 0x001Fu, 0xFFFFFFFFu)] // all index 3
    [InlineData(0x07E0u, 0x0000u, 0x55555555u)] // green vs black, all index 1
    [InlineData(0xFFFFu, 0xFFFEu, 0xAAAAAAAAu)] // near-equal endpoints, all index 2
    [InlineData(0x8410u, 0x0421u, 0x12345678u)] // arbitrary mid-range
    public void FourColorMode_MatchesVendored(ushort c0, ushort c1, uint indices)
    {
        Assert.True(c0 > c1, "test precondition: 4-color mode requires c0 > c1");
        AssertParity(MakeBlock(c0, c1, indices), useAlpha: false);
        AssertParity(MakeBlock(c0, c1, indices), useAlpha: true);  // useAlpha is ignored in 4-color mode
    }

    // c0 <= c1 -> 3-color + black mode. useAlpha controls whether index 3 is transparent.
    [Theory]
    [InlineData(0x0000u, 0xFFFFu, 0xE4E4E4E4u)]
    [InlineData(0x0000u, 0x0000u, 0xFFFFFFFFu)] // both endpoints zero, all index 3
    [InlineData(0x0421u, 0x8410u, 0xAAAAAAAAu)] // c0 < c1, all index 2 (interpolated half)
    [InlineData(0x001Fu, 0xF800u, 0x12345678u)]
    [InlineData(0x07E0u, 0x07E0u, 0x55555555u)] // c0 == c1, all index 1
    public void ThreeColorMode_NoAlpha_MatchesVendored(ushort c0, ushort c1, uint indices)
    {
        Assert.True(c0 <= c1, "test precondition: 3-color mode requires c0 <= c1");
        AssertParity(MakeBlock(c0, c1, indices), useAlpha: false);
    }

    [Theory]
    [InlineData(0x0000u, 0xFFFFu, 0xE4E4E4E4u)]
    [InlineData(0x0000u, 0x0000u, 0xFFFFFFFFu)] // index 3 -> transparent black
    [InlineData(0x0421u, 0x8410u, 0x12345678u)]
    [InlineData(0x001Fu, 0xF800u, 0xAAAAAAAAu)]
    public void ThreeColorMode_WithAlpha_MatchesVendored(ushort c0, ushort c1, uint indices)
    {
        Assert.True(c0 <= c1, "test precondition: 3-color mode requires c0 <= c1");
        AssertParity(MakeBlock(c0, c1, indices), useAlpha: true);
    }

    [Fact]
    public void Fuzz_RandomBlocks_AllParityChecks()
    {
        var rng = new Random(0xC0FFEE);
        for (int i = 0; i < 2000; i++)
        {
            ushort c0 = (ushort)rng.Next(0x10000);
            ushort c1 = (ushort)rng.Next(0x10000);
            uint indices = (uint)rng.Next() ^ ((uint)rng.Next() << 1);
            bool useAlpha = (i & 1) == 0;
            var block = MakeBlock(c0, c1, indices);
            try
            {
                AssertParity(block, useAlpha);
            }
            catch (Exception)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Parity failure: c0=0x{c0:X4} c1=0x{c1:X4} indices=0x{indices:X8} useAlpha={useAlpha}");
            }
        }
    }

    [Fact]
    public void Fuzz_EveryFiveBitChannelEndpoint_WithFixedIndices()
    {
        // Cover every possible R5 endpoint pair (32x32 = 1024 cases) with green=0, blue=0.
        // Catches off-by-one bugs in 5-bit -> 8-bit replication.
        for (int r0 = 0; r0 < 32; r0++)
        {
            for (int r1 = 0; r1 < 32; r1++)
            {
                ushort c0 = (ushort)(r0 << 11);
                ushort c1 = (ushort)(r1 << 11);
                AssertParity(MakeBlock(c0, c1, 0xE4E4E4E4u), useAlpha: false);
                AssertParity(MakeBlock(c0, c1, 0xE4E4E4E4u), useAlpha: true);
            }
        }
    }

    [Fact]
    public void Fuzz_EverySixBitGreenEndpoint_WithFixedIndices()
    {
        // Cover every possible G6 endpoint pair (64x64) for the 6-bit green channel.
        for (int g0 = 0; g0 < 64; g0++)
        {
            for (int g1 = 0; g1 < 64; g1++)
            {
                ushort c0 = (ushort)(g0 << 5);
                ushort c1 = (ushort)(g1 << 5);
                AssertParity(MakeBlock(c0, c1, 0xE4E4E4E4u), useAlpha: false);
            }
        }
    }
}
