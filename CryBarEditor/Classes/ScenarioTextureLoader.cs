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
    const int SliceBytes = TargetSize * TargetSize * 4;

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
        // The returned PooledBuffer is owned by the caller.
        public async ValueTask<PooledBuffer?> TryResolveAsync(string textureName, CancellationToken ct = default)
        {
            if (_index is null || string.IsNullOrEmpty(textureName)) return null;

            var fname = Path.GetFileName(textureName.Replace('\\', '/'));
            if (fname.Length == 0) return null;

            var entries = _index.Find(fname + "_basecolor.ddt");
            if (entries.Count == 0) entries = _index.Find(fname + ".ddt");
            if (entries.Count == 0) return null;

            PooledBuffer? buf = null;
            try
            {
                buf = await _read(entries[0]);
                if (buf is null) return null;
                ct.ThrowIfCancellationRequested();
                var owned = buf;
                buf = null;
                return owned;
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
            finally { buf?.Dispose(); }
        }
    }

    sealed class SliceBuffer(int sliceIndex, PooledBuffer rgba) : IDisposable
    {
        public int SliceIndex { get; } = sliceIndex;
        public PooledBuffer Rgba { get; } = rgba;
        public void Dispose() => Rgba.Dispose();
    }

    public sealed record LoadProgress(int Resolved, int Decoded, int Uploaded, int Total);

    // Top-level pipeline:
    //   1) resolve names to DDT byte buffers via FileIndex (parallel; CachedBarFile
    //      serializes per-stream access internally)
    //   2) decode all resolved buffers to RGBA8 in parallel
    //   3) upload each decoded slice via the GL-thread callback in encounter order
    public static async Task LoadAllAsync(
        ScenarioPreviewData data,
        NameResolver resolver,
        Func<int, ReadOnlyMemory<byte>, Task> uploadSliceAsync,
        Action<LoadProgress>? onProgress,
        CancellationToken ct)
    {
        var names = data.TextureSet.Names;
        int total = names.Count;

        var resolved = new PooledBuffer?[total];
        var buffers = new SliceBuffer?[total];

        try
        {
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
            await Parallel.ForAsync(0, total, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async (i, innerCt) =>
            {
                innerCt.ThrowIfCancellationRequested();
                var src = resolved[i];
                if (src is not null && src.Length > 0)
                {
                    var dst = new PooledBuffer(SliceBytes);
                    bool ok = false;
                    try
                    {
                        ok = await DDTImage.DecodeBaseColorOnlyIntoAsync(src.Memory, TargetSize, dst.Memory, innerCt);
                        if (ok) buffers[i] = new SliceBuffer(i, dst);
                    }
                    finally { if (!ok) dst.Dispose(); }
                }
                var n = Interlocked.Increment(ref decodedSoFar);
                onProgress?.Invoke(new LoadProgress(total, n, 0, total));
            });

            for (int i = 0; i < total; i++)
            {
                resolved[i]?.Dispose();
                resolved[i] = null;
            }

            int uploaded = 0;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var slice = buffers[i];
                if (slice is not null)
                {
                    try
                    {
                        await uploadSliceAsync(slice.SliceIndex, slice.Rgba.Memory);
                        data.MarkSliceReady(slice.SliceIndex);
                    }
                    finally
                    {
                        slice.Dispose();
                        buffers[i] = null;
                    }
                }
                uploaded++;
                onProgress?.Invoke(new LoadProgress(total, total, uploaded, total));
            }
        }
        finally
        {
            for (int i = 0; i < total; i++)
            {
                resolved[i]?.Dispose();
                buffers[i]?.Dispose();
            }
        }
    }
}
