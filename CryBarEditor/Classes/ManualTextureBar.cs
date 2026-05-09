using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CryBar.Bar;
using CryBar.Utilities;

namespace CryBarEditor.Classes;

// Wraps a BAR opened ad-hoc as a fallback texture source when the
// auto-discovered FileIndex doesn't contain a referenced terrain texture.
// Lifetime: one scenario load. Disposed when the load finishes or cancels.
internal sealed class ManualTextureBar : IDisposable
{
    readonly CachedBarFile _bar;

    ManualTextureBar(CachedBarFile bar) => _bar = bar;

    public static ManualTextureBar? TryOpen(string path)
    {
        FileStream? stream = null;
        try
        {
            stream = File.OpenRead(path);
            var bar = new BarFile(stream);
            if (!bar.Load(out _))
            {
                stream.Dispose();
                return null;
            }
            return new ManualTextureBar(new CachedBarFile(bar, stream));
        }
        catch
        {
            stream?.Dispose();
            return null;
        }
    }

    public async ValueTask<PooledBuffer?> ResolveTextureAsync(string textureName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(textureName)) return null;

        var entries = _bar.Bar.Entries;
        if (entries is null) return null;

        var match = FindBySuffix(entries, textureName + "_basecolor.ddt")
                  ?? FindBySuffix(entries, textureName + ".ddt");
        if (match is null) return null;

        ct.ThrowIfCancellationRequested();

        try
        {
            return await _bar.ReadEntryDecompressedPooledAsync(match);
        }
        catch
        {
            return null;
        }
    }

    // Compare case-insensitively, treating both '\' and '/' as separators.
    static BarFileEntry? FindBySuffix(IReadOnlyList<BarFileEntry> entries, string suffix)
    {
        var normForward = suffix.Replace('\\', '/');
        for (int i = 0; i < entries.Count; i++)
        {
            var path = entries[i].RelativePath;
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return entries[i];
            if (path.Replace('\\', '/').EndsWith(normForward, StringComparison.OrdinalIgnoreCase))
                return entries[i];
        }
        return null;
    }

    public void Dispose() => _bar.Dispose();
}
