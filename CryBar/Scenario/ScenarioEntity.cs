using System.Numerics;

namespace CryBar.Scenario;

public sealed class ScenarioEntity
{
    public required uint EntityId { get; init; }
    public required int ProtoIndex { get; set; }
    public required string ProtoName { get; set; }
    public required byte PlayerId { get; set; }
    public required Vector3 Position { get; set; }
    public required Matrix3x3 Rotation { get; set; }

    // EN body header bytes from h1[6] (after EN magic + enSize) up to start of Position.
    // PlayerId lives at offset 8 within this blob (the u32 player_id field).
    // Length = posOff - 6 (i.e. 28 if unk_len=12, 32 if unk_len=16).
    public required byte[] H1Prefix { get; init; }

    // Bytes inside the EN section that come AFTER the rotation matrix.
    // 0 bytes for new-format scenarios; 1 byte (optional ignore13 bool) for old format.
    public required byte[] H1EnTail { get; init; }

    // Bytes AFTER the EN section ends, to end of H1 record.
    // Contains UnitP1 (with name_index = ProtoIndex), UnitP2, markers, 2x fake_p1.
    public required byte[] H1Suffix { get; init; }
}

public readonly struct Matrix3x3 : System.IEquatable<Matrix3x3>
{
    public readonly float M11, M12, M13;
    public readonly float M21, M22, M23;
    public readonly float M31, M32, M33;

    public Matrix3x3(
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
    {
        M11 = m11; M12 = m12; M13 = m13;
        M21 = m21; M22 = m22; M23 = m23;
        M31 = m31; M32 = m32; M33 = m33;
    }

    public static Matrix3x3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Matrix3x3 FromYawDegrees(float yawDeg)
    {
        float r = yawDeg * (float)System.Math.PI / 180f;
        float c = (float)System.Math.Cos(r);
        float s = (float)System.Math.Sin(r);
        // Yaw around Y (game is Y-up). Standard right-handed rotation matrix:
        //   [ c  0  s ]
        //   [ 0  1  0 ]
        //   [-s  0  c ]
        return new Matrix3x3(c, 0, s, 0, 1, 0, -s, 0, c);
    }

    public float ExtractYawDegrees()
    {
        // For a yaw-only matrix, M13 = sin(yaw), M11 = cos(yaw).
        return (float)(System.Math.Atan2(M13, M11) * 180.0 / System.Math.PI);
    }

    public Vector3 Multiply(Vector3 v) => new(
        M11 * v.X + M12 * v.Y + M13 * v.Z,
        M21 * v.X + M22 * v.Y + M23 * v.Z,
        M31 * v.X + M32 * v.Y + M33 * v.Z);

    public bool Equals(Matrix3x3 other) =>
        M11 == other.M11 && M12 == other.M12 && M13 == other.M13 &&
        M21 == other.M21 && M22 == other.M22 && M23 == other.M23 &&
        M31 == other.M31 && M32 == other.M32 && M33 == other.M33;

    public override bool Equals(object? obj) => obj is Matrix3x3 m && Equals(m);
    public override int GetHashCode()
    {
        var h = new System.HashCode();
        h.Add(M11); h.Add(M12); h.Add(M13);
        h.Add(M21); h.Add(M22); h.Add(M23);
        h.Add(M31); h.Add(M32); h.Add(M33);
        return h.ToHashCode();
    }
    public static bool operator ==(Matrix3x3 a, Matrix3x3 b) => a.Equals(b);
    public static bool operator !=(Matrix3x3 a, Matrix3x3 b) => !a.Equals(b);
}
