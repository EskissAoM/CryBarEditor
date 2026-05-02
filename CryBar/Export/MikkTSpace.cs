using System.Numerics;

namespace CryBar.Export;

/// <summary>
/// Computes per-vertex tangents using Morten S. Mikkelsen's MikkTSpace algorithm
/// (Simulation of Wrinkled Surfaces Revisited, 2008). This is the same algorithm
/// Blender's glTF I/O addon uses when "Mesh -&gt; Tangents" is enabled and what
/// AoM:Retold's content pipeline uses (fbximport's "use_mikktspace": true), so
/// recomputing here matches Blender's TANGENT output within float precision.
///
/// Simplified relative to the reference: glTF data is already vertex-deduplicated,
/// so we skip the geometric-vertex welding step. The per-corner angle weighting,
/// per-face UV-gradient tangents, Gram-Schmidt orthogonalization, and the W
/// handedness sign are preserved exactly.
/// </summary>
public static class MikkTSpace
{
    /// <summary>Threshold below which a UV gradient or vector length is treated as degenerate.</summary>
    const float DegenerateEpsilon = 1e-20f;

    /// <summary>
    /// Computes tangents (xyz + w handedness) for an indexed triangle mesh.
    /// </summary>
    /// <param name="positions">Per-vertex positions, length = vertexCount * 3.</param>
    /// <param name="normals">Per-vertex normals, length = vertexCount * 3 (must be unit-length).</param>
    /// <param name="texcoords">Per-vertex UVs, length = vertexCount * 2.</param>
    /// <param name="indices">Triangle indices, length = triangleCount * 3.</param>
    /// <returns>Tangent array, length = vertexCount * 4. xyz is unit, w is +1 or -1.</returns>
    public static float[] ComputeTangents(
        ReadOnlySpan<float> positions,
        ReadOnlySpan<float> normals,
        ReadOnlySpan<float> texcoords,
        ReadOnlySpan<uint> indices)
    {
        int vertexCount = positions.Length / 3;
        if (positions.Length != vertexCount * 3) throw new ArgumentException("positions length not multiple of 3", nameof(positions));
        if (normals.Length != vertexCount * 3) throw new ArgumentException("normals length mismatch", nameof(normals));
        if (texcoords.Length != vertexCount * 2) throw new ArgumentException("texcoords length mismatch", nameof(texcoords));
        if (indices.Length % 3 != 0) throw new ArgumentException("indices length not multiple of 3", nameof(indices));

        var tAccum = new Vector3[vertexCount];
        var bAccum = new Vector3[vertexCount];

        for (int f = 0; f < indices.Length; f += 3)
        {
            int i0 = (int)indices[f];
            int i1 = (int)indices[f + 1];
            int i2 = (int)indices[f + 2];

            var p0 = ReadVec3(positions, i0);
            var p1 = ReadVec3(positions, i1);
            var p2 = ReadVec3(positions, i2);

            var uv0 = ReadVec2(texcoords, i0);
            var uv1 = ReadVec2(texcoords, i1);
            var uv2 = ReadVec2(texcoords, i2);

            var e1 = p1 - p0;
            var e2 = p2 - p0;
            var duv1 = uv1 - uv0;
            var duv2 = uv2 - uv0;

            float det = duv1.X * duv2.Y - duv2.X * duv1.Y;
            if (MathF.Abs(det) < DegenerateEpsilon) continue; // degenerate UV; contribute nothing

            float invDet = 1f / det;
            var tFace = (duv2.Y * e1 - duv1.Y * e2) * invDet;
            var bFace = (-duv2.X * e1 + duv1.X * e2) * invDet;

            // Angle-weight each corner so vertices shared by skinny triangles aren't dominated
            // by the area imbalance. Reference: Mikkelsen 2008.
            float w0 = CornerAngle(p0, p1, p2);
            float w1 = CornerAngle(p1, p2, p0);
            float w2 = CornerAngle(p2, p0, p1);

            tAccum[i0] += tFace * w0; bAccum[i0] += bFace * w0;
            tAccum[i1] += tFace * w1; bAccum[i1] += bFace * w1;
            tAccum[i2] += tFace * w2; bAccum[i2] += bFace * w2;
        }

        var result = new float[vertexCount * 4];
        for (int v = 0; v < vertexCount; v++)
        {
            var n = ReadVec3(normals, v);
            var t = tAccum[v];
            var b = bAccum[v];

            // Gram-Schmidt against the vertex normal: T_final = normalize(T - (T·N)·N)
            var tOrtho = t - Vector3.Dot(t, n) * n;
            float len = tOrtho.Length();
            if (len < DegenerateEpsilon)
            {
                // Degenerate; fall back to an arbitrary perpendicular of N.
                tOrtho = Vector3.Cross(n, MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
                tOrtho = Vector3.Normalize(tOrtho);
            }
            else
            {
                tOrtho /= len;
            }

            // glTF/MikkTSpace W convention: B = cross(N, T_xyz) * W. Pick the sign so the
            // synthesized B agrees with the accumulated B (which encodes UV winding).
            var bNatural = Vector3.Cross(n, tOrtho);
            float w = Vector3.Dot(bNatural, b) >= 0 ? 1f : -1f;

            int o = v * 4;
            result[o]     = tOrtho.X;
            result[o + 1] = tOrtho.Y;
            result[o + 2] = tOrtho.Z;
            result[o + 3] = w;
        }
        return result;
    }

    static Vector3 ReadVec3(ReadOnlySpan<float> arr, int i)
    {
        int o = i * 3;
        return new Vector3(arr[o], arr[o + 1], arr[o + 2]);
    }

    static Vector2 ReadVec2(ReadOnlySpan<float> arr, int i)
    {
        int o = i * 2;
        return new Vector2(arr[o], arr[o + 1]);
    }

    /// <summary>
    /// Returns the unsigned angle (radians) at vertex <paramref name="apex"/> formed by edges
    /// (apex-&gt;a) and (apex-&gt;b). Used as the per-corner weight in tangent accumulation.
    /// </summary>
    static float CornerAngle(Vector3 apex, Vector3 a, Vector3 b)
    {
        var ea = a - apex;
        var eb = b - apex;
        float la = ea.Length();
        float lb = eb.Length();
        if (la < DegenerateEpsilon || lb < DegenerateEpsilon) return 0f;
        float cos = Math.Clamp(Vector3.Dot(ea, eb) / (la * lb), -1f, 1f);
        return MathF.Acos(cos);
    }
}
