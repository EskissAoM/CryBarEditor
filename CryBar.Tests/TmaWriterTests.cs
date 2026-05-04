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

    [Fact]
    public void Write_AnimWithBindPoseRotation_ComposesCorrectly()
    {
        var bones = new[]
        {
            new GlbBone { Name = "root", ParentIndex = -1,
                LocalMatrix = MatrixDecomp.Compose(new System.Numerics.Vector3(0,1,0), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One),
                InverseBindMatrix = Identity16() },
        };
        var anim = new GlbAnimation
        {
            Name = "test", Duration = 0.1f, FrameCount = 2,
            Tracks =
            [
                new GlbBoneTrack
                {
                    BoneIndex = 0,
                    Translations = [new System.Numerics.Vector3(0,1,0), new System.Numerics.Vector3(1,1,0)],
                    Rotations = [System.Numerics.Quaternion.Identity, System.Numerics.Quaternion.Identity],
                }
            ]
        };

        var (tma, _) = TmaWriter.Write(anim, bones, null);
        var parsed = new TmaFile(tma);
        Assert.True(parsed.Parsed);
        var decoded = CryBar.TMM.TmaDecoder.DecodeAllTracks(parsed)!;
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded[0].Translations.Length);
    }

    [Fact]
    public void Write_ResamplesNonMatchingFrameCount()
    {
        var bones = new[]
        {
            new GlbBone { Name = "root", ParentIndex = -1, LocalMatrix = Identity16(), InverseBindMatrix = Identity16() },
        };
        var anim = new GlbAnimation
        {
            Name = "test", Duration = 2.0f, FrameCount = 3,
            Tracks =
            [
                new GlbBoneTrack
                {
                    BoneIndex = 0,
                    Translations = [
                        new System.Numerics.Vector3(0,0,0),
                        new System.Numerics.Vector3(0.25f,0,0),
                        new System.Numerics.Vector3(0.5f,0,0),
                        new System.Numerics.Vector3(0.75f,0,0),
                        new System.Numerics.Vector3(1,0,0)
                    ],
                    Rotations = Enumerable.Repeat(System.Numerics.Quaternion.Identity, 5).ToArray(),
                }
            ]
        };

        var (tma, _) = TmaWriter.Write(anim, bones, null);
        var parsed = new TmaFile(tma);
        Assert.Equal(3u, parsed.FrameCount);
    }

    [Fact]
    public void Write_StaticTrack_EmitsConstantEncoding()
    {
        // No track => SampleTrack falls back to bind pose for every frame, all 30 are equal.
        var bones = new[]
        {
            new GlbBone { Name = "root", ParentIndex = -1, LocalMatrix = Identity16(), InverseBindMatrix = Identity16() },
        };
        var anim = new GlbAnimation
        {
            Name = "stand",
            Duration = 1.0f,
            FrameCount = 30,
            Tracks = [],
        };
        var (tma, _) = TmaWriter.Write(anim, bones, null);
        var parsed = new TmaFile(tma);
        Assert.True(parsed.Parsed);
        Assert.Equal(TmaEncoding.Constant, parsed.Tracks![0].TranslationEncoding);
        Assert.Equal(TmaEncoding.Constant, parsed.Tracks[0].RotationEncoding);
        Assert.Equal(TmaEncoding.Constant, parsed.Tracks[0].ScaleEncoding);
        var decoded = TmaDecoder.DecodeAllTracks(parsed)!;
        Assert.Equal(30, decoded[0].Translations.Length);
        Assert.Equal(30, decoded[0].Rotations.Length);
    }

    [Fact]
    public void Write_AnimatedTrack_EmitsRawAndQuat64()
    {
        var bones = new[]
        {
            new GlbBone { Name = "root", ParentIndex = -1, LocalMatrix = Identity16(), InverseBindMatrix = Identity16() },
        };
        var anim = new GlbAnimation
        {
            Name = "wiggle",
            Duration = 0.1f,
            FrameCount = 5,
            Tracks =
            [
                new GlbBoneTrack
                {
                    BoneIndex = 0,
                    Translations = [
                        new System.Numerics.Vector3(0, 0, 0),
                        new System.Numerics.Vector3(0.1f, 0, 0),
                        new System.Numerics.Vector3(0.2f, 0, 0),
                        new System.Numerics.Vector3(0.3f, 0, 0),
                        new System.Numerics.Vector3(0.4f, 0, 0),
                    ],
                    Rotations = Enumerable.Repeat(System.Numerics.Quaternion.Identity, 5).ToArray(),
                }
            ]
        };
        var (tma, _) = TmaWriter.Write(anim, bones, null);
        var parsed = new TmaFile(tma);
        Assert.Equal(TmaEncoding.Raw, parsed.Tracks![0].TranslationEncoding);
        Assert.Equal(TmaEncoding.Constant, parsed.Tracks[0].RotationEncoding);

        // TmaWriter mirrors X back to game space, so the stored value is -input.
        var decoded = TmaDecoder.DecodeAllTracks(parsed)!;
        Assert.Equal(5, decoded[0].Translations.Length);
        Assert.NotEqual(decoded[0].Translations[0].X, decoded[0].Translations[4].X);
        for (int f = 1; f < 5; f++)
        {
            float delta = MathF.Abs(decoded[0].Translations[f].X - decoded[0].Translations[f - 1].X);
            Assert.InRange(delta, 0.099f, 0.101f);
        }
    }

    static float[] Identity16() => [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
}
