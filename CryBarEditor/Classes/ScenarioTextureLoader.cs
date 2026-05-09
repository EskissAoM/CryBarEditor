using System;
using System.Collections.Generic;
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
        readonly ResolveFromManualBarAsync? _manualBarResolver;

        public delegate ValueTask<PooledBuffer?> ResolveFromManualBarAsync(string textureName, CancellationToken ct);

        public NameResolver(FileIndex index, ReadEntryAsync read, ResolveFromManualBarAsync? manualBarResolver = null)
        {
            _index = index;
            _read = read;
            _manualBarResolver = manualBarResolver;
        }

        // Resolve a "<group>\<name>" terrain reference to decompressed DDT bytes.
        // Returned PooledBuffer is owned by the caller.
        // Uses FindByPartialPath so name collisions across groups
        // (e.g. default/black_rock vs egyptian/black_rock) resolve correctly.
        public async ValueTask<PooledBuffer?> TryResolveAsync(string textureName, CancellationToken ct = default)
        {
            if (_index is null || string.IsNullOrEmpty(textureName)) return null;

            var entries = _index.FindByPartialPath(textureName + "_basecolor.ddt");
            if (entries.Count == 0) entries = _index.FindByPartialPath(textureName + ".ddt");
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

        public async ValueTask<(PooledBuffer? Buffer, TextureSource Source)> TryResolveWithSourceAsync(
            string textureName, CancellationToken ct = default)
        {
            var fromIndex = await TryResolveAsync(textureName, ct);
            if (fromIndex is not null)
                return (fromIndex, TextureSource.Index);

            if (_manualBarResolver is not null)
            {
                var fromBar = await _manualBarResolver(textureName, ct);
                if (fromBar is not null)
                    return (fromBar, TextureSource.ManualBar);
            }

            return (null, TextureSource.Placeholder);
        }
    }

    sealed class SliceBuffer(int sliceIndex, PooledBuffer rgba) : IDisposable
    {
        public int SliceIndex { get; } = sliceIndex;
        public PooledBuffer Rgba { get; } = rgba;
        public void Dispose() => Rgba.Dispose();
    }

    public sealed record LoadProgress(int Resolved, int Decoded, int Uploaded, int Total);

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
            var sourceFlags = new TextureSource[total];
            await Parallel.ForAsync(0, total, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async (i, innerCt) =>
            {
                var (buf, source) = await resolver.TryResolveWithSourceAsync(names[i], innerCt);
                resolved[i] = buf;
                sourceFlags[i] = source;
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

                    // Decode failure renders as the green placeholder; downgrade so
                    // the inspector counts it as missing.
                    if (!ok) sourceFlags[i] = TextureSource.Placeholder;
                }
                var n = Interlocked.Increment(ref decodedSoFar);
                onProgress?.Invoke(new LoadProgress(total, n, 0, total));
            });

            // Run after the decode phase so decode failures count as missing.
            for (int i = 0; i < total; i++)
                data.SetTextureSource(i, sourceFlags[i]);

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
