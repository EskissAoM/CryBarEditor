using CryBar.Indexing;
using CryBar.Utilities;
using CryBarEditor.Classes;

namespace CryBar.Tests;

public class ScenarioTextureLoaderTests
{
    static ValueTask<PooledBuffer?> ReadFails(FileIndexEntry _) =>
        ValueTask.FromResult<PooledBuffer?>(null);

    [Fact]
    public async Task Resolve_UnknownName_ReturnsNull()
    {
        var index = new FileIndex();
        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadFails);

        using var buf = await resolver.TryResolveAsync("nonexistent\\name");

        Assert.Null(buf);
    }

    [Fact]
    public async Task Resolve_EmptyName_ReturnsNull()
    {
        var index = new FileIndex();
        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadFails);

        using var buf = await resolver.TryResolveAsync("");

        Assert.Null(buf);
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
        using var buf = await resolver.TryResolveAsync("greek\\greek_grass_1");

        Assert.NotNull(buf);
        Assert.Equal(payload, buf.Span.ToArray());
    }
}
