using System.Text;
using System.Text.Json;

using CryBar.Export;

namespace CryBar.Tests;

public class FbximportEmitterTests
{
    static JsonDocument Parse(byte[] padded)
    {
        var trimmed = FbximportReader.TrimNulPadding(padded);
        return JsonDocument.Parse(Encoding.UTF8.GetString(trimmed));
    }

    [Fact]
    public void EmitForTmm_Static_NoAttachments_HasExpectedShape()
    {
        var bytes = FbximportEmitter.EmitForTmm(null, hasSkin: false);
        Assert.Equal(FbximportEmitter.PaddedSize, bytes.Length);

        using var doc = Parse(bytes);
        var root = doc.RootElement;
        Assert.Equal("static", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("adjustment_transform", out _));
        Assert.False(root.GetProperty("include_vertex_colors").GetBoolean());
        Assert.True(root.GetProperty("use_mikktspace").GetBoolean());
        Assert.Equal("", root.GetProperty("skeleton").GetString());
        Assert.Equal(0, root.GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public void EmitForTmm_Skeletal_WithAttachment_DecomposesMatrix()
    {
        var tmm = new GlbExtras.TmmSection
        {
            Attachments =
            [
                new GlbExtras.AttachmentEntry
                {
                    Name = "weapon",
                    LocalMatrix =
                    [
                        1, 0, 0,   // row 0: rotation
                        0, 1, 0,
                        0, 0, 1,
                        2.5f, 1.0f, 0.5f, // translation
                    ],
                    ForcedDummyBoneName = "WeaponPoint",
                }
            ],
        };

        var bytes = FbximportEmitter.EmitForTmm(tmm, hasSkin: true);
        using var doc = Parse(bytes);
        var root = doc.RootElement;
        Assert.Equal("skeletal", root.GetProperty("type").GetString());

        var atts = root.GetProperty("attachments");
        Assert.Equal(1, atts.GetArrayLength());
        var a = atts[0];
        Assert.Equal("weapon", a.GetProperty("dummy").GetString());
        Assert.Equal("WeaponPoint", a.GetProperty("ForcedDummyBoneName").GetString());

        var t = a.GetProperty("transform").GetProperty("t");
        Assert.Equal(2.5f, t[0].GetSingle(), 5);
        Assert.Equal(1.0f, t[1].GetSingle(), 5);
        Assert.Equal(0.5f, t[2].GetSingle(), 5);

        var s = a.GetProperty("transform").GetProperty("s");
        Assert.Equal(1f, s[0].GetSingle(), 5);
    }

    [Fact]
    public void EmitForTma_WithVisibilityController_RoundTripsToJson()
    {
        var tma = new GlbExtras.TmaSection
        {
            OriginalFrameCount = 30,
            Controllers =
            [
                new GlbExtras.TmaControllerEntry
                {
                    Type = 1,
                    Start = 0.32f,
                    End = 0.65f,
                    EaseIn = 0.0f,
                    EaseOut = 0.0f,
                    InvertLogic = true,
                    AttachPointName = "arrow",
                },
                // Footprint should NOT appear in fbximport output
                new GlbExtras.TmaControllerEntry
                {
                    Type = 2,
                    SpawnTime = 0.5f,
                    AttachPointName = "LeftFoot",
                },
            ],
        };

        var bytes = FbximportEmitter.EmitForTma(tma, duration: 1.5f);
        using var doc = Parse(bytes);
        var root = doc.RootElement;
        Assert.Equal("animation", root.GetProperty("type").GetString());
        Assert.Equal(1.5f, root.GetProperty("override_animation_length").GetSingle());

        var ctrls = root.GetProperty("animation_controllers");
        Assert.Equal(1, ctrls.GetArrayLength());
        var c = ctrls[0];
        Assert.Equal("attach_point_visibility", c.GetProperty("type").GetString());
        Assert.Equal(0.32f, c.GetProperty("start_time").GetSingle());
        Assert.Equal("arrow", c.GetProperty("attachpoint").GetString());
        Assert.True(c.GetProperty("invert_logic").GetBoolean());
    }

    [Fact]
    public void EmitForTma_NoControllers_EmitsEmptyArray()
    {
        var bytes = FbximportEmitter.EmitForTma(null, duration: 0f);
        using var doc = Parse(bytes);
        var ctrls = doc.RootElement.GetProperty("animation_controllers");
        Assert.Equal(0, ctrls.GetArrayLength());
        Assert.Equal(1.0f, doc.RootElement.GetProperty("override_animation_length").GetSingle());
    }
}
