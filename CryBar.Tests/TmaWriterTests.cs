using CryBar.Export;
using CryBar.TMM;

namespace CryBar.Tests;

public class TmaWriterTests
{
    [Fact]
    public void Write_NoTracks_ProducesParseableTma()
    {
        var anim = new GlbAnimation
        {
            Name = "idle",
            Duration = 1.0f,
            FrameCount = 30,
            Tracks = [],
        };
        var bones = new[]
        {
            new GlbBone { Name = "root", ParentIndex = -1, LocalMatrix = Identity16(), InverseBindMatrix = Identity16() },
        };
        var (tma, warnings) = TmaWriter.Write(anim, bones, extras: null);
        var parsed = new TmaFile(tma);
        Assert.True(parsed.Parsed);
        Assert.Equal(30u, parsed.FrameCount);
        Assert.Equal(1, (int)parsed.NumBones);
    }

    static float[] Identity16() => [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
}
