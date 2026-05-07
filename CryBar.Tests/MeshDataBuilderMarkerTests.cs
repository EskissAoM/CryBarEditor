using System.Numerics;
using CryBar.TMM;
using CryBarEditor.Classes;

namespace CryBar.Tests;

public class MeshDataBuilderMarkerTests
{
    static TmmAttachment MakeAttachment(string name, int parentBoneId, float[] localMatrix4x3)
    {
        return new TmmAttachment
        {
            TypeFlag = 0,
            ParentBoneId = parentBoneId,
            Name = name,
            AdjustmentTransformMatrix = new float[12],
            LocalTransformMatrix = localMatrix4x3,
            DummyBoneMode = 0,
            DummyBoneTransformMode = 0
        };
    }

    static float[] Identity4x3WithTranslation(float tx, float ty, float tz) =>
        new float[]
        {
            1, 0, 0, tx,
            0, 1, 0, ty,
            0, 0, 1, tz
        };

    [Fact]
    public void BuildMarkers_AttachmentTranslation_IsXNegated()
    {
        var att = MakeAttachment("AttachA", -1, Identity4x3WithTranslation(2f, 3f, 4f));
        var (markers, _) = MeshDataBuilder.BuildMarkers(new[] { att }, [], null);

        Assert.Single(markers);
        Assert.Equal("AttachA", markers[0].Name);
        Assert.True(markers[0].HasOrientation);
        Assert.Equal(-2f, markers[0].Position.X, 4);
        Assert.Equal(3f, markers[0].Position.Y, 4);
        Assert.Equal(4f, markers[0].Position.Z, 4);
    }

    [Fact]
    public void BuildMarkers_AttachmentAxes_AreUnitAndXNegated()
    {
        // Local axes: X=(2,0,0) (will normalize to (1,0,0) then X-negate to (-1,0,0))
        var localMatrix = new float[]
        {
            2, 0, 0, 0,
            0, 2, 0, 0,
            0, 0, 2, 0
        };
        var att = MakeAttachment("Scaled", -1, localMatrix);
        var (markers, _) = MeshDataBuilder.BuildMarkers(new[] { att }, [], null);

        Assert.Equal(-1f, markers[0].AxisX.X, 4);
        Assert.Equal(0f, markers[0].AxisX.Y, 4);
        Assert.Equal(0f, markers[0].AxisX.Z, 4);

        Assert.Equal(0f, markers[0].AxisY.X, 4);
        Assert.Equal(1f, markers[0].AxisY.Y, 4);
    }

    [Fact]
    public void BuildMarkers_NoAttachments_ReturnsEmpty()
    {
        var (markers, impacts) = MeshDataBuilder.BuildMarkers([], [], null);
        Assert.Empty(markers);
        Assert.Empty(impacts);
    }

    [Fact]
    public void BuildMarkers_ImpactPoints_AreXNegatedAndNamed()
    {
        var info = new TmmAutoAttachInfo
        {
            ManualImpactPoints = new[]
            {
                new float[] { 1f, 2f, 3f, 0f },
                new float[] { -4f, 5f, -6f, 0f }
            }
        };

        var (_, impacts) = MeshDataBuilder.BuildMarkers([], [], info);

        Assert.Equal(2, impacts.Length);
        Assert.Equal("ImpactPoint_0", impacts[0].Name);
        Assert.False(impacts[0].HasOrientation);
        Assert.Equal(-1f, impacts[0].Position.X, 4);
        Assert.Equal(2f, impacts[0].Position.Y, 4);
        Assert.Equal(3f, impacts[0].Position.Z, 4);

        Assert.Equal("ImpactPoint_1", impacts[1].Name);
        Assert.Equal(4f, impacts[1].Position.X, 4);
    }

    [Fact]
    public void BuildMarkers_InvalidParentBoneId_FallsBackToLocalSpace()
    {
        var att = MakeAttachment("Orphan", parentBoneId: 999, Identity4x3WithTranslation(1f, 0f, 0f));
        var (markers, _) = MeshDataBuilder.BuildMarkers(new[] { att }, [], null);
        Assert.Equal(-1f, markers[0].Position.X, 4);
    }
}
