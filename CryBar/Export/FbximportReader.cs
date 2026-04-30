using System.Text;
using System.Text.Json;
using CryBar.TMM;

namespace CryBar.Export;

/// <summary>
/// Parses fbximport JSON files. The official format pads each file to a fixed size
/// with NUL bytes after the closing brace; this reader handles that transparently.
/// Currently only animation_controllers are extracted - other fbximport fields are
/// authoring-time metadata not used by our pipeline.
/// </summary>
public static class FbximportReader
{
    /// <summary>
    /// Returns visibility/footprint controllers from an animation-type fbximport.
    /// Returns null when the file is missing/invalid; returns empty array when the
    /// file parses but has no controllers.
    /// </summary>
    public static GlbExtras.TmaControllerEntry[]? ParseAnimationControllers(ReadOnlySpan<byte> fileBytes)
    {
        var trimmed = TrimNulPadding(fileBytes);
        if (trimmed.IsEmpty) return null;

        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(trimmed));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("animation_controllers", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<GlbExtras.TmaControllerEntry>(arr.GetArrayLength());
            foreach (var c in arr.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                var typeStr = c.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : null;
                if (typeStr != FbximportEmitter.AttachPointVisibilityType) continue;

                var entry = new GlbExtras.TmaControllerEntry { Type = TmaControllerType.Visibility };
                if (c.TryGetProperty("start_time", out var s)) entry.Start = s.GetSingle();
                if (c.TryGetProperty("end_time", out var e)) entry.End = e.GetSingle();
                if (c.TryGetProperty("ease_in_time", out var ei)) entry.EaseIn = ei.GetSingle();
                if (c.TryGetProperty("ease_out_time", out var eo)) entry.EaseOut = eo.GetSingle();
                if (c.TryGetProperty("invert_logic", out var il)) entry.InvertLogic = il.GetBoolean();
                if (c.TryGetProperty("attachpoint", out var ap)) entry.AttachPointName = ap.GetString() ?? "";
                list.Add(entry);
            }
            return list.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Strips trailing NUL bytes that the game writes to pad fbximport files to a fixed size.</summary>
    public static ReadOnlySpan<byte> TrimNulPadding(ReadOnlySpan<byte> bytes)
    {
        int len = bytes.Length;
        while (len > 0 && bytes[len - 1] == 0) len--;
        return bytes[..len];
    }
}
