using System.Numerics;
using System.Text;
using System.Text.Json;
using CryBar.TMM;

namespace CryBar.Export;

/// <summary>
/// Generates .fbximport JSON files matching the AoM:Retold Model Convert Tool format.
/// Output is a stub: only fields recoverable from TMM/TMA + extras are populated.
/// Authoring-time fields (physicsobjectsettings, footprint_settings, EnableRayTracingForModel,
/// destructionoverrides intervals, attachments.preview/DummyBoneMode/ForcedDummyBoneName)
/// use safe defaults so the file is valid input to the official Model Convert Tool.
/// </summary>
public static class FbximportEmitter
{
    /// <summary>Game files are NUL-padded to a fixed size after the closing brace.</summary>
    public const int PaddedSize = 2048;

    /// <summary>The only animation_controllers.type value documented in vanilla fbximport samples.</summary>
    public const string AttachPointVisibilityType = "attach_point_visibility";

    /// <summary>
    /// Emits a static or skeletal fbximport. <paramref name="hasSkin"/> selects "skeletal"
    /// when true, "static" otherwise. Attachments are written from extras with bone="",
    /// preview="" since neither is recoverable from a TMM round-trip.
    /// </summary>
    public static byte[] EmitForTmm(GlbExtras.TmmSection? tmm, bool hasSkin)
    {
        using var ms = new MemoryStream();
        using (var w = CreateWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", hasSkin ? "skeletal" : "static");
            WriteAdjustmentTransform(w);
            w.WriteBoolean("include_vertex_colors", false);
            w.WriteBoolean("use_mikktspace", true);
            w.WriteString("skeleton", "");
            WriteTmmAttachments(w, tmm);
            w.WriteEndObject();
        }
        return PadToFixedSize(ms.ToArray());
    }

    /// <summary>
    /// Emits an animation fbximport. animation_controllers is populated from
    /// extras (visibility/type-1 only - footprint controllers have no fbximport mapping).
    /// </summary>
    public static byte[] EmitForTma(GlbExtras.TmaSection? tma, float duration)
    {
        using var ms = new MemoryStream();
        using (var w = CreateWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "animation");
            WriteAdjustmentTransform(w);
            w.WriteBoolean("include_vertex_colors", false);
            w.WriteBoolean("use_mikktspace", true);
            w.WriteNumber("Shared_Animation_Bucket_Count", 0);
            w.WriteString("skeleton", "");
            w.WriteStartArray("attachments");
            w.WriteEndArray();
            w.WriteNumber("resample_animation_frames", 0);
            w.WriteBoolean("resample_animation_looped", true);
            w.WriteNumber("override_animation_length", duration > 0 ? duration : 1.0f);

            w.WriteStartArray("destructionoverrides");
            w.WriteStartObject();
            w.WriteBoolean("usecustomintervaldata", false);
            w.WriteStartArray("intervals");
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();

            WriteAnimationControllers(w, tma);
            w.WriteEndObject();
        }
        return PadToFixedSize(ms.ToArray());
    }

    static Utf8JsonWriter CreateWriter(Stream s) =>
        new(s, new JsonWriterOptions { Indented = true });

    static void WriteAdjustmentTransform(Utf8JsonWriter w)
    {
        w.WriteStartObject("adjustment_transform");
        WriteVec3(w, "t", 0, 0, 0);
        WriteVec3(w, "r", 0, 0, 0);
        WriteVec3(w, "s", 1, 1, 1);
        w.WriteEndObject();
    }

    static void WriteVec3(Utf8JsonWriter w, string name, float x, float y, float z)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(x);
        w.WriteNumberValue(y);
        w.WriteNumberValue(z);
        w.WriteEndArray();
    }

    static void WriteTmmAttachments(Utf8JsonWriter w, GlbExtras.TmmSection? tmm)
    {
        w.WriteStartArray("attachments");
        if (tmm?.Attachments is { Length: > 0 })
        {
            foreach (var a in tmm.Attachments)
            {
                w.WriteStartObject();
                w.WriteString("dummy", a.Name);
                w.WriteString("bone", "");
                w.WriteString("preview", "");
                WriteTransformFromMatrix12(w, a.LocalMatrix);
                if (!string.IsNullOrEmpty(a.ForcedDummyBoneName))
                    w.WriteString("ForcedDummyBoneName", a.ForcedDummyBoneName);
                w.WriteEndObject();
            }
        }
        w.WriteEndArray();
    }

    /// <summary>
    /// Decomposes a 12-element row-major 4x3 affine matrix into translation, euler-angle
    /// rotation (degrees), and scale. The fbximport uses YXZ euler order (degrees).
    /// </summary>
    static void WriteTransformFromMatrix12(Utf8JsonWriter w, float[] m12)
    {
        w.WriteStartObject("transform");
        if (m12.Length == 12)
        {
            // Build a 4x4 matrix with identity for the missing column
            var m = new Matrix4x4(
                m12[0],  m12[1],  m12[2],  0,
                m12[3],  m12[4],  m12[5],  0,
                m12[6],  m12[7],  m12[8],  0,
                m12[9],  m12[10], m12[11], 1);

            if (Matrix4x4.Decompose(m, out var s, out var r, out var t))
            {
                var euler = QuaternionToEulerDegrees(r);
                WriteVec3(w, "t", t.X, t.Y, t.Z);
                WriteVec3(w, "r", euler.X, euler.Y, euler.Z);
                WriteVec3(w, "s", s.X, s.Y, s.Z);
                w.WriteEndObject();
                return;
            }
        }
        WriteVec3(w, "t", 0, 0, 0);
        WriteVec3(w, "r", 0, 0, 0);
        WriteVec3(w, "s", 1, 1, 1);
        w.WriteEndObject();
    }

    /// <summary>
    /// XYZ euler angles in degrees from a unit quaternion. Order matches the convention
    /// observed in vanilla fbximport samples (e.g., toxotes_iron arrow attachment).
    /// </summary>
    static Vector3 QuaternionToEulerDegrees(Quaternion q)
    {
        // ZYX intrinsic / XYZ extrinsic
        float sinR = 2f * (q.W * q.X + q.Y * q.Z);
        float cosR = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinR, cosR);

        float sinP = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinP) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinP)
            : MathF.Asin(sinP);

        float sinY = 2f * (q.W * q.Z + q.X * q.Y);
        float cosY = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(sinY, cosY);

        const float r2d = 180f / MathF.PI;
        return new Vector3(roll * r2d, pitch * r2d, yaw * r2d);
    }

    static void WriteAnimationControllers(Utf8JsonWriter w, GlbExtras.TmaSection? tma)
    {
        w.WriteStartArray("animation_controllers");
        if (tma?.Controllers is { Length: > 0 })
        {
            foreach (var c in tma.Controllers)
            {
                if (c.Type != TmaControllerType.Visibility) continue;
                w.WriteStartObject();
                w.WriteString("type", AttachPointVisibilityType);
                w.WriteNumber("start_time", c.Start);
                w.WriteNumber("end_time", c.End);
                w.WriteNumber("ease_in_time", c.EaseIn);
                w.WriteNumber("ease_out_time", c.EaseOut);
                w.WriteBoolean("invert_logic", c.InvertLogic);
                w.WriteString("attachpoint", c.AttachPointName);
                w.WriteEndObject();
            }
        }
        w.WriteEndArray();
    }

    static byte[] PadToFixedSize(byte[] json)
    {
        if (json.Length >= PaddedSize) return json;
        var padded = new byte[PaddedSize];
        Buffer.BlockCopy(json, 0, padded, 0, json.Length);
        return padded;
    }
}
