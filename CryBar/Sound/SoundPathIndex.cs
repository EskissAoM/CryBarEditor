using System.IO;

namespace CryBar.Sound;

/// <summary>
/// Maps a sound file's stem (filename without extension, case-insensitive) to the relative
/// path(s) it appears at. Built from <see cref="SoundManifestEntry"/> filenames (soundmanifest.xml)
/// and <see cref="SoundsetSound"/> filenames (soundsets_*.soundset). Used to recover file paths
/// for FMOD bank items (events/subsounds), whose names match these stems.
/// <br/>
/// A stem can map to multiple paths (e.g. the same clip referenced from several cinematics). When
/// the manifest supplies a duration per path, <see cref="ResolveBest"/> disambiguates by matching
/// it against the audio's own duration; otherwise callers fall back to the first candidate.
/// </summary>
public class SoundPathIndex
{
    /// <summary>A registered path and (when known) its duration in milliseconds.</summary>
    readonly record struct PathEntry(string Path, int? LengthMs);

    readonly Dictionary<string, List<PathEntry>> _byStem = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Max ms a candidate's manifest duration may differ from the audio's duration to still count
    /// as a match. Real matches are exact to the ms; this only guards against asserting a wrong
    /// path when no candidate truly lines up.
    /// </summary>
    const int DurationToleranceMs = 150;

    public int Count => _byStem.Count;

    /// <summary>Filename stem keyed on: backslash-normalized, extension stripped.</summary>
    public static string GetStem(string path) => Path.GetFileNameWithoutExtension(path.Replace('/', '\\'));

    /// <summary>
    /// Registers a relative sound path (e.g. "music\aotg_theme\x.wav"), keyed by its filename stem,
    /// optionally with its duration in ms (used to disambiguate same-named paths). Duplicate paths
    /// are ignored, but a later duration enriches an earlier path that had none. Blank paths skipped.
    /// </summary>
    public void Add(string relativePath, int? lengthMs = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        // Normalize to backslashes so stem extraction and stored paths are consistent.
        var normalized = relativePath.Replace('/', '\\');
        var stem = GetStem(normalized);
        if (string.IsNullOrEmpty(stem)) return;

        if (!_byStem.TryGetValue(stem, out var list))
            _byStem[stem] = list = new List<PathEntry>();

        for (int i = 0; i < list.Count; i++)
        {
            if (!string.Equals(list[i].Path, normalized, StringComparison.OrdinalIgnoreCase)) continue;
            // Same path already present - fill in a duration if we now have one.
            if (list[i].LengthMs == null && lengthMs != null)
                list[i] = list[i] with { LengthMs = lengthMs };
            return;
        }

        list.Add(new PathEntry(normalized, lengthMs));
    }

    /// <summary>First relative path registered for the given name (its stem), or null if none.</summary>
    public string? Resolve(string name)
        => _byStem.TryGetValue(GetStem(name), out var list) && list.Count > 0 ? list[0].Path : null;

    /// <summary>All relative paths registered for the given name (its stem). Empty if none.</summary>
    public IReadOnlyList<string> ResolveAll(string name)
        => _byStem.TryGetValue(GetStem(name), out var list)
            ? list.ConvertAll(e => e.Path)
            : [];

    /// <summary>
    /// Best path for the given name, disambiguated by duration: among same-named paths, the one
    /// whose manifest duration matches <paramref name="lengthMs"/> most closely. Falls back to the
    /// first candidate when there's a single candidate, no usable duration, or no close match.
    /// </summary>
    public string? ResolveBest(string name, int lengthMs)
    {
        if (!_byStem.TryGetValue(GetStem(name), out var list) || list.Count == 0) return null;
        if (list.Count == 1 || lengthMs <= 0) return list[0].Path;

        string? best = null;
        int bestDelta = int.MaxValue;
        foreach (var e in list)
        {
            if (e.LengthMs is not int l) continue;
            int delta = Math.Abs(l - lengthMs);
            if (delta < bestDelta) { bestDelta = delta; best = e.Path; }
        }

        return best != null && bestDelta <= DurationToleranceMs ? best : list[0].Path;
    }

    /// <summary>True when a name maps to more than one distinct path.</summary>
    public bool IsAmbiguous(string name)
        => _byStem.TryGetValue(GetStem(name), out var list) && list.Count > 1;

    public void Clear() => _byStem.Clear();

    /// <summary>
    /// Builds a combined index from soundmanifest entries (with durations) and parsed soundset
    /// definitions. Either source may be null/empty.
    /// </summary>
    public static SoundPathIndex BuildFrom(
        IEnumerable<SoundManifestEntry>? manifest,
        IEnumerable<SoundsetDefinition>? soundsets)
    {
        var index = new SoundPathIndex();

        if (manifest != null)
            foreach (var entry in manifest)
                index.Add(entry.Filename, entry.Length is double s ? (int)Math.Round(s * 1000) : null);

        if (soundsets != null)
            foreach (var def in soundsets)
                foreach (var sound in def.Sounds)
                    index.Add(sound.Filename);

        return index;
    }
}
