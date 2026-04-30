using System.Numerics;
using CryBar.Export;

namespace CryBar.Tests;

public class MatrixDecompTests
{
    [Fact]
    public void Compose_DecomposeRoundTrip_PreservesValues()
    {
        var t = new Vector3(1, 2, 3);
        var r = Quaternion.CreateFromYawPitchRoll(0.5f, 0.3f, 0.1f);
        var s = new Vector3(2, 3, 4);

        var m = MatrixDecomp.Compose(t, r, s);
        MatrixDecomp.Decompose(m, out var t2, out var r2, out var s2);

        Assert.Equal(t.X, t2.X, 1e-5f);
        Assert.Equal(t.Y, t2.Y, 1e-5f);
        Assert.Equal(t.Z, t2.Z, 1e-5f);
        Assert.Equal(s.X, s2.X, 1e-5f);
        Assert.Equal(s.Y, s2.Y, 1e-5f);
        Assert.Equal(s.Z, s2.Z, 1e-5f);
        Assert.Equal(r.X, r2.X, 1e-5f);
        Assert.Equal(r.Y, r2.Y, 1e-5f);
        Assert.Equal(r.Z, r2.Z, 1e-5f);
        Assert.Equal(r.W, r2.W, 1e-5f);
    }
}
