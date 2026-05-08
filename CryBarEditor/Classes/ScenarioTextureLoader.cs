using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CryBar;
using CryBar.Bar;
using CryBar.Indexing;
using CryBar.Utilities;

namespace CryBarEditor.Classes;

public sealed class ScenarioTextureLoader
{
    public const int TargetSize = 256;

    public delegate ValueTask<PooledBuffer?> ReadEntryAsync(FileIndexEntry entry);

    public sealed class NameResolver
    {
        readonly FileIndex _index;
        readonly ReadEntryAsync _read;

        public NameResolver(FileIndex index, ReadEntryAsync read)
        {
            _index = index;
            _read = read;
        }

        // Resolve a "<group>\<name>" terrain reference to decompressed DDT bytes.
        // Probes "<name>_basecolor.ddt" then "<name>.ddt" via FileIndex (stem-flexible).
        public async ValueTask<byte[]?> TryResolveAsync(string textureName, CancellationToken ct = default)
        {
            if (_index is null || string.IsNullOrEmpty(textureName)) return null;

            var fname = Path.GetFileName(textureName.Replace('\\', '/'));
            if (fname.Length == 0) return null;

            var entries = _index.Find(fname + "_basecolor.ddt");
            if (entries.Count == 0) entries = _index.Find(fname + ".ddt");
            if (entries.Count == 0) return null;

            try
            {
                using var buf = await _read(entries[0]);
                if (buf is null) return null;

                ct.ThrowIfCancellationRequested();
                return buf.Span.ToArray();
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return null;
            }
        }
    }
}
