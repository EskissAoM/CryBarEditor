using System.Numerics;
using CryBar.Scenario;
using Xunit;

namespace CryBar.Tests;

public class ScenarioEntityTests
{
    [Fact]
    public void Construct_AssignsAllFields()
    {
        var e = new ScenarioEntity
        {
            EntityId = 7,
            ProtoIndex = 12,
            ProtoName = "villager",
            PlayerId = 3,
            Position = new Vector3(1, 2, 3),
            Rotation = Matrix3x3.Identity,
            H1Prefix = [],
            H1EnTail = [],
            H1Suffix = new byte[] { 0xAA, 0xBB }
        };
        Assert.Equal(7u, e.EntityId);
        Assert.Equal(12, e.ProtoIndex);
        Assert.Equal("villager", e.ProtoName);
        Assert.Equal(3, e.PlayerId);
        Assert.Equal(new Vector3(1, 2, 3), e.Position);
        Assert.Equal(Matrix3x3.Identity, e.Rotation);
        Assert.Equal([], e.H1Prefix);
        Assert.Equal([], e.H1EnTail);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, e.H1Suffix);
    }

    [Fact]
    public void Matrix3x3_Identity_IsRowMajor111Diagonal()
    {
        var m = Matrix3x3.Identity;
        Assert.Equal(1, m.M11); Assert.Equal(0, m.M12); Assert.Equal(0, m.M13);
        Assert.Equal(0, m.M21); Assert.Equal(1, m.M22); Assert.Equal(0, m.M23);
        Assert.Equal(0, m.M31); Assert.Equal(0, m.M32); Assert.Equal(1, m.M33);
    }

    [Fact]
    public void Matrix3x3_FromYawDegrees_RotatesAroundY()
    {
        // Yaw 90 deg around Y: world +X (1,0,0) -> -Z (0,0,-1)
        var m = Matrix3x3.FromYawDegrees(90f);
        var v = new Vector3(1, 0, 0);
        var r = m.Multiply(v);
        Assert.True(System.Math.Abs(r.X) < 1e-5f, $"X={r.X}");
        Assert.True(System.Math.Abs(r.Y) < 1e-5f, $"Y={r.Y}");
        Assert.True(System.Math.Abs(r.Z + 1f) < 1e-5f, $"Z={r.Z}");
    }

    [Fact]
    public void Matrix3x3_ExtractYawDegrees_RoundTripsForYawOnlyMatrix()
    {
        var m = Matrix3x3.FromYawDegrees(45f);
        var yaw = m.ExtractYawDegrees();
        Assert.True(System.Math.Abs(yaw - 45f) < 1e-3f, $"yaw={yaw}");
    }
}
