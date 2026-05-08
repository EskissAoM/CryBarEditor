using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CryBar;
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

    public sealed record SliceBuffer(int SliceIndex, byte[] Rgba);
    public sealed record LoadProgress(int Resolved, int Decoded, int Uploaded, int Total);

    // Top-level pipeline:
    //   1) resolve names to DDT byte buffers via FileIndex (parallel; CachedBarFile
    //      serializes per-stream access internally)
    //   2) decode all resolved buffers to RGBA8 in parallel
    //   3) upload each decoded slice via the GL-thread callback in encounter order
    public static async Task LoadAllAsync(
        ScenarioPreviewData data,
        NameResolver resolver,
        Func<int, byte[], Task> uploadSliceAsync,
        Action<LoadProgress>? onProgress,
        CancellationToken ct)
    {
        var names = data.TextureSet.Names;
        int total = names.Count;

        var resolved = new byte[]?[total];
        int resolvedSoFar = 0;
        await Parallel.ForAsync(0, total, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, async (i, innerCt) =>
        {
            resolved[i] = await resolver.TryResolveAsync(names[i], innerCt);
            var n = Interlocked.Increment(ref resolvedSoFar);
            onProgress?.Invoke(new LoadProgress(n, 0, 0, total));
        });

        int decodedSoFar = 0;
        var buffers = new SliceBuffer?[total];
        await Parallel.ForAsync(0, total, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, async (i, innerCt) =>
        {
            innerCt.ThrowIfCancellationRequested();
            var bytes = resolved[i];
            if (bytes is not null && bytes.Length > 0)
            {
                var rgba = await DDTImage.DecodeBaseColorOnlyAsync(bytes, TargetSize, innerCt);
                if (rgba is not null) buffers[i] = new SliceBuffer(i, rgba);
            }
            var n = Interlocked.Increment(ref decodedSoFar);
            onProgress?.Invoke(new LoadProgress(total, n, 0, total));
        });

        int uploaded = 0;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var buf = buffers[i];
            if (buf is not null)
            {
                await uploadSliceAsync(buf.SliceIndex, buf.Rgba);
                data.SliceReady[buf.SliceIndex] = true;
            }
            uploaded++;
            onProgress?.Invoke(new LoadProgress(total, total, uploaded, total));
        }
    }
}
