using System.Numerics;
using CryBar.Export;
using CryBar.TMM;

namespace CryBar.Tests;

public class Quat64PackingTests
{
    const float DriftTolerance = 1e-4f;

    [Fact]
    public void Identity_RoundTripsExactly()
    {
        var q = Quaternion.Identity;
        AssertRoundTripWithinTolerance(q);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 0f)]
    [InlineData(0f, 1f, 0f, 0f)]
    [InlineData(0f, 0f, 1f, 0f)]
    // (0,0,0,1) is identity, covered above
    [InlineData(-1f, 0f, 0f, 0f)]
    [InlineData(0f, -1f, 0f, 0f)]
    [InlineData(0f, 0f, -1f, 0f)]
    public void PureAxisQuaternions_RoundTripWithinTolerance(float x, float y, float z, float w)
    {
        AssertRoundTripWithinTolerance(new Quaternion(x, y, z, w));
    }

    [Theory]
    // 90deg around X = (sin45, 0, 0, cos45)
    [InlineData(0.7071068f, 0, 0, 0.7071068f)]
    // 90deg around Y
    [InlineData(0, 0.7071068f, 0, 0.7071068f)]
    // 90deg around Z
    [InlineData(0, 0, 0.7071068f, 0.7071068f)]
    // -90deg around X
    [InlineData(-0.7071068f, 0, 0, 0.7071068f)]
    public void AxisAligned90Deg_RoundTripsWithinTolerance(float x, float y, float z, float w)
    {
        AssertRoundTripWithinTolerance(new Quaternion(x, y, z, w));
    }

    [Fact]
    public void Fuzz1000RandomQuaternions_RoundTripWithinTolerance()
    {
        var rng = new Random(42);  // seeded for reproducibility
        for (int i = 0; i < 1000; i++)
        {
            var q = Quaternion.Normalize(new Quaternion(
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1)));
            AssertRoundTripWithinTolerance(q);
        }
    }

    [Fact]
    public void Fuzz_NearAxisAligned_RoundTripWithinTolerance()
    {
        // Near-pure-axis quaternions: largest component is one axis with tiny perturbations
        var rng = new Random(43);
        for (int i = 0; i < 200; i++)
        {
            var nearX = Quaternion.Normalize(new Quaternion(
                0.999f + (float)(rng.NextDouble() * 0.001),
                (float)(rng.NextDouble() * 0.04 - 0.02),
                (float)(rng.NextDouble() * 0.04 - 0.02),
                (float)(rng.NextDouble() * 0.04 - 0.02)));
            AssertRoundTripWithinTolerance(nearX);

            var nearW = Quaternion.Normalize(new Quaternion(
                (float)(rng.NextDouble() * 0.04 - 0.02),
                (float)(rng.NextDouble() * 0.04 - 0.02),
                (float)(rng.NextDouble() * 0.04 - 0.02),
                0.999f + (float)(rng.NextDouble() * 0.001)));
            AssertRoundTripWithinTolerance(nearW);
        }
    }

    [Fact]
    public void Idempotence_DecodeEncodeDecodeIsFixedPoint()
    {
        // Take a random valid packed value, decode, encode, decode again.
        // The two decoded quaternions should match within tolerance.
        var rng = new Random(44);
        for (int i = 0; i < 200; i++)
        {
            // Generate via encoding a random unit quat (so the packed value is in-range)
            var q0 = Quaternion.Normalize(new Quaternion(
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1)));
            ulong p1 = TmaWriter.EncodeQuat64(q0);
            var q1 = TmaDecoder.DecodeSmallestThree64(p1);
            ulong p2 = TmaWriter.EncodeQuat64(q1);
            var q2 = TmaDecoder.DecodeSmallestThree64(p2);

            // Decode-then-encode-then-decode is a fixed point modulo quantization noise
            AssertQuaternionsClose(q1, q2, DriftTolerance);
        }
    }


    static void AssertRoundTripWithinTolerance(Quaternion q)
    {
        ulong packed = TmaWriter.EncodeQuat64(q);
        var roundTripped = TmaDecoder.DecodeSmallestThree64(packed);
        AssertQuaternionsClose(q, roundTripped, DriftTolerance);
    }

    static void AssertQuaternionsClose(Quaternion a, Quaternion b, float tol)
    {
        // Quaternions q and -q represent the same rotation, so check both signs.
        float drift = MathF.Min(
            MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) +
                      (a.Z - b.Z) * (a.Z - b.Z) + (a.W - b.W) * (a.W - b.W)),
            MathF.Sqrt((a.X + b.X) * (a.X + b.X) + (a.Y + b.Y) * (a.Y + b.Y) +
                      (a.Z + b.Z) * (a.Z + b.Z) + (a.W + b.W) * (a.W + b.W)));
        Assert.True(drift < tol, $"Quaternion drift {drift} exceeds tolerance {tol}: a=({a.X},{a.Y},{a.Z},{a.W}) b=({b.X},{b.Y},{b.Z},{b.W})");
    }
}
