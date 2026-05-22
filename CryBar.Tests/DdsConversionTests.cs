using CryBar.Bar;
using CryBar.BCnEncoder.Encoder;
using CryBar.BCnEncoder.Shared;
using CryBar.BCnEncoder.Shared.ImageFiles;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CryBar.Tests;

public class DdsConversionTests
{
    [Fact]
    public void IsConvertibleExtension_includes_dds()
    {
        Assert.True(ConversionHelper.IsConvertibleExtension(".dds"));
        Assert.True(ConversionHelper.IsConvertibleExtension(".DDS"));
    }

    [Fact]
    public void GetConvertedExtension_dds_returns_png()
    {
        Assert.Equal(".png", ConversionHelper.GetConvertedExtension(".dds"));
        Assert.Equal(".png", ConversionHelper.GetConvertedExtension(".DDS"));
    }

    static byte[] MakeTestDdsBytes(CompressionFormat fmt = CompressionFormat.Bc3, int w = 4, int h = 4)
    {
        var pixels = new ColorRgba32[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new ColorRgba32(255, 0, 255, 255);
        var enc = new BcEncoder(fmt);
        enc.OutputOptions.FileFormat = OutputFileFormat.Dds;
        enc.OutputOptions.GenerateMipMaps = false;
        var pixelBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
        var dds = enc.EncodeToDds(pixelBytes, w, h, PixelFormat.Rgba32);
        using var ms = new MemoryStream();
        dds.Write(ms);
        return ms.ToArray();
    }

    [Fact]
    public void GetDdsSummary_reads_dimensions_and_format()
    {
        var bytes = MakeTestDdsBytes();
        var summary = ConversionHelper.GetDdsSummary(bytes);
        Assert.NotNull(summary);
        Assert.Equal(4, summary!.Value.Width);
        Assert.Equal(4, summary.Value.Height);
        Assert.False(string.IsNullOrEmpty(summary.Value.FormatName));
    }

    [Fact]
    public async Task DecodeDdsToImage_returns_pixels_matching_source()
    {
        var bytes = MakeTestDdsBytes();
        using var image = await ConversionHelper.DecodeDdsToImage(bytes);
        Assert.NotNull(image);
        Assert.Equal(4, image!.Width);
        Assert.Equal(4, image.Height);
        Rgba32 pixel = default;
        image.ProcessPixelRows(accessor => { pixel = accessor.GetRowSpan(0)[0]; });
        Assert.True(pixel.R > 200 && pixel.G < 50 && pixel.B > 200,
            $"Expected magenta-ish, got ({pixel.R},{pixel.G},{pixel.B})");
    }

    [Fact]
    public async Task DecodeDdsToImage_returns_null_for_garbage()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var image = await ConversionHelper.DecodeDdsToImage(bytes);
        Assert.Null(image);
    }

    [Fact]
    public async Task ConvertDdsToPngBytes_produces_decodable_png()
    {
        var dds = MakeTestDdsBytes();
        var png = await ConversionHelper.ConvertDdsToPngBytes(dds);
        Assert.NotNull(png);
        using var image = Image.Load<Rgba32>(png!);
        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
    }

    [Fact]
    public async Task ConvertDdsToPngBytes_returns_null_for_garbage()
    {
        var png = await ConversionHelper.ConvertDdsToPngBytes(new byte[] { 1, 2, 3, 4 });
        Assert.Null(png);
    }

    [Fact]
    public async Task EncodeImageToDdsBytes_roundtrips_through_decoder()
    {
        using var src = new Image<Rgba32>(8, 8);
        src.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = new Rgba32(255, 128, 0, 255);
            }
        });

        var ddsBytes = await ConversionHelper.EncodeImageToDdsBytes(
            src, CompressionFormat.Bc7, sRgb: false, mipmapLevels: 1);
        Assert.NotEmpty(ddsBytes);

        using var decoded = await ConversionHelper.DecodeDdsToImage(ddsBytes);
        Assert.NotNull(decoded);
        Assert.Equal(8, decoded!.Width);
        Assert.Equal(8, decoded.Height);

        Rgba32 pixel = default;
        decoded.ProcessPixelRows(accessor => { pixel = accessor.GetRowSpan(0)[0]; });
        Assert.InRange(pixel.R, 230, 255);
        Assert.InRange(pixel.G, 100, 160);
        Assert.InRange(pixel.B, 0, 30);
    }

    [Fact]
    public async Task EncodeImageToDdsBytes_srgb_sets_srgb_dxgi_format()
    {
        using var src = new Image<Rgba32>(4, 4);
        var ddsBytes = await ConversionHelper.EncodeImageToDdsBytes(
            src, CompressionFormat.Bc7, sRgb: true, mipmapLevels: 1);

        using var ms = new MemoryStream(ddsBytes);
        var dds = DdsFile.Load(ms);
        Assert.True(dds.header.ddsPixelFormat.IsDxt10Format);
        Assert.Equal(DxgiFormat.DxgiFormatBc7UnormSrgb, dds.dx10Header.dxgiFormat);
    }
}
