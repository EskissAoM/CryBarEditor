using System.Numerics;
using System.Text;

namespace CryBar.Export;

/// <summary>Shared low-level binary write helpers used by TmmWriter and TmaWriter.</summary>
internal static class TmmWriteHelpers
{
    internal static void WriteUtf16String(BinaryWriter w, string s)
    {
        w.Write(s.Length);
        if (s.Length > 0)
            w.Write(Encoding.Unicode.GetBytes(s));
    }

    // Applies F*M*F (F = diag(-1,1,1,1)) then writes 16 floats in TMM/TMA's flat convention.
    // F*M*F negates entries where exactly one of (row, col) is 0; entry [0,0] is double-negated.
    // Storage convention: TMM/TMA (and the python reference) stores col-major flat of the col-vector
    // matrix, which is identical to row-major flat of the equivalent System.Numerics row-vector
    // matrix (col i of M_col = row i of M_col^T = row i of M_sn). Hence we serialize System.Numerics
    // fields in row-major order: M11..M14, M21..M24, M31..M34, M41..M44.
    internal static void WriteMatrix4x4Fmf(BinaryWriter w, Matrix4x4 m)
    {
        var r = new Matrix4x4(
             m.M11, -m.M12, -m.M13, -m.M14,
            -m.M21,  m.M22,  m.M23,  m.M24,
            -m.M31,  m.M32,  m.M33,  m.M34,
            -m.M41,  m.M42,  m.M43,  m.M44);

        w.Write(r.M11); w.Write(r.M12); w.Write(r.M13); w.Write(r.M14);
        w.Write(r.M21); w.Write(r.M22); w.Write(r.M23); w.Write(r.M24);
        w.Write(r.M31); w.Write(r.M32); w.Write(r.M33); w.Write(r.M34);
        w.Write(r.M41); w.Write(r.M42); w.Write(r.M43); w.Write(r.M44);
    }
}
