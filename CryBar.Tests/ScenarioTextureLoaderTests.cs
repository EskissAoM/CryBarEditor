using CryBar.Indexing;
using CryBar.Utilities;
using CryBarEditor.Classes;

namespace CryBar.Tests;

public class ScenarioTextureLoaderTests
{
    static ValueTask<PooledBuffer?> ReadFails(FileIndexEntry _) =>
        ValueTask.FromResult<PooledBuffer?>(null);

    [Fact]
    public async Task Resolve_UnknownName_ReturnsPlaceholder()
    {
        var index = new FileIndex();
        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadFails);

        var (buf, source) = await resolver.TryResolveWithSourceAsync("nonexistent\\name");

        Assert.Null(buf);
        Assert.Equal(TextureSource.Placeholder, source);
    }

    [Fact]
    public async Task Resolve_EmptyName_ReturnsPlaceholder()
    {
        var index = new FileIndex();
        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadFails);

        var (buf, source) = await resolver.TryResolveWithSourceAsync("");

        Assert.Null(buf);
        Assert.Equal(TextureSource.Placeholder, source);
    }

    [Fact]
    public async Task Resolve_IndexHit_DelegatesToReadEntry_AndReturnsBytes()
    {
        var index = new FileIndex();
        index.Add(new FileIndexEntry
        {
            FullRelativePath = "art/terrain/greek/greek_grass_1_basecolor.ddt",
            Source = FileIndexSource.RootFile
        });

        var payload = new byte[] { 1, 2, 3, 4, 5 };
        ValueTask<PooledBuffer?> Stub(FileIndexEntry _)
        {
            var pb = new PooledBuffer(payload.Length);
            payload.CopyTo(pb.Span);
            return ValueTask.FromResult<PooledBuffer?>(pb);
        }

        var resolver = new ScenarioTextureLoader.NameResolver(index, Stub);
        var (buf, source) = await resolver.TryResolveWithSourceAsync("greek\\greek_grass_1");

        Assert.NotNull(buf);
        Assert.Equal(TextureSource.Index, source);
        Assert.Equal(payload, buf!.Span.ToArray());
        buf.Dispose();
    }

    [Fact]
    public async Task Resolve_GroupPrefix_DisambiguatesCollisions()
    {
        var index = new FileIndex();
        index.Add(new FileIndexEntry
        {
            FullRelativePath = "art/terrain/zzz_other/black_rock_basecolor.ddt",
            Source = FileIndexSource.RootFile
        });
        index.Add(new FileIndexEntry
        {
            FullRelativePath = "art/terrain/default/black_rock_basecolor.ddt",
            Source = FileIndexSource.RootFile
        });
        index.Add(new FileIndexEntry
        {
            FullRelativePath = "art/terrain/egyptian/black_rock_basecolor.ddt",
            Source = FileIndexSource.RootFile
        });

        var defaultPayload = new byte[] { 1, 2, 3 };
        var egyptianPayload = new byte[] { 9, 9, 9 };

        ValueTask<PooledBuffer?> Stub(FileIndexEntry e)
        {
            var pb = new PooledBuffer(3);
            var src = e.FullRelativePath.ToString().Contains("default", StringComparison.OrdinalIgnoreCase)
                ? defaultPayload : egyptianPayload;
            src.CopyTo(pb.Span);
            return ValueTask.FromResult<PooledBuffer?>(pb);
        }

        var resolver = new ScenarioTextureLoader.NameResolver(index, Stub);
        var (buf, source) = await resolver.TryResolveWithSourceAsync("default\\black_rock");

        Assert.NotNull(buf);
        Assert.Equal(TextureSource.Index, source);
        Assert.Equal(defaultPayload, buf!.Span.ToArray());
        buf.Dispose();
    }

    [Fact]
    public async Task Resolve_FallbackBar_UsedWhenIndexMisses()
    {
        var index = new FileIndex();    // empty
        var fallbackBytes = new byte[] { 7, 8, 9 };

        ValueTask<PooledBuffer?> ManualStub(string name, CancellationToken ct)
        {
            Assert.Equal("default\\black_rock", name);
            var pb = new PooledBuffer(fallbackBytes.Length);
            fallbackBytes.CopyTo(pb.Span);
            return ValueTask.FromResult<PooledBuffer?>(pb);
        }

        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadFails, ManualStub);
        var (buf, source) = await resolver.TryResolveWithSourceAsync("default\\black_rock");

        Assert.NotNull(buf);
        Assert.Equal(TextureSource.ManualBar, source);
        Assert.Equal(fallbackBytes, buf!.Span.ToArray());
        buf.Dispose();
    }

    [Fact]
    public async Task Resolve_FallbackBar_NotUsedWhenIndexHits()
    {
        var index = new FileIndex();
        index.Add(new FileIndexEntry
        {
            FullRelativePath = "art/terrain/default/black_rock_basecolor.ddt",
            Source = FileIndexSource.RootFile
        });
        var indexBytes = new byte[] { 1, 2, 3 };

        ValueTask<PooledBuffer?> IndexStub(FileIndexEntry _)
        {
            var pb = new PooledBuffer(indexBytes.Length);
            indexBytes.CopyTo(pb.Span);
            return ValueTask.FromResult<PooledBuffer?>(pb);
        }
        ValueTask<PooledBuffer?> ManualStub(string _, CancellationToken __)
            => throw new InvalidOperationException("Manual BAR must not be consulted when index hits");

        var resolver = new ScenarioTextureLoader.NameResolver(index, IndexStub, ManualStub);
        var (buf, source) = await resolver.TryResolveWithSourceAsync("default\\black_rock");

        Assert.NotNull(buf);
        Assert.Equal(TextureSource.Index, source);
        Assert.Equal(indexBytes, buf!.Span.ToArray());
        buf.Dispose();
    }

    [Fact]
    public async Task Resolve_FallbackBar_ReturnsPlaceholderWhenManualMissesToo()
    {
        var index = new FileIndex();    // empty

        static ValueTask<PooledBuffer?> ManualMisses(string _, CancellationToken __)
            => ValueTask.FromResult<PooledBuffer?>(null);

        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadFails, ManualMisses);
        var (buf, source) = await resolver.TryResolveWithSourceAsync("default\\nothing");

        Assert.Null(buf);
        Assert.Equal(TextureSource.Placeholder, source);
    }
}
