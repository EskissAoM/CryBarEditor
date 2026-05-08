using CryBar.Bar;
using CryBar.Indexing;
using CryBar.Scenario;
using CryBar.Utilities;
using CryBarEditor.Classes;

namespace CryBar.Tests;

[Collection("Integration")]
public class ScenarioTextureLoaderIntegrationTests
{
    static string GamePath =>
        Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";

    // Reads a FileIndexEntry by reopening its BAR fresh -- fine for diagnostics, no caching needed.
    static async ValueTask<PooledBuffer?> ReadEntry(FileIndexEntry entry)
    {
        if (entry.Source == FileIndexSource.RootFile || entry.BarFilePath is null)
            return null;

        using var stream = File.OpenRead(entry.BarFilePath);
        var bar = new BarFile(stream);
        if (!bar.Load(out _)) return null;

        var rel = entry.EntryRelativePath.ToString();
        var be = bar.Entries!.FirstOrDefault(e =>
            e.RelativePath.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (be is null) return null;

        var raw = be.ReadDataDecompressedPooled(stream);
        if (raw is null) return null;
        await Task.CompletedTask;
        return raw;
    }

    [SkippableFact]
    public async Task FixtureNames_ResolveAgainstArtTerrainTextures()
    {
        Skip.IfNot(Directory.Exists(GamePath), "Game install not found");
        var barPath = Path.Combine(GamePath, "art", "ArtTerrainTextures.bar");
        Skip.IfNot(File.Exists(barPath), "ArtTerrainTextures.bar not found");

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var compressed = File.ReadAllBytes(fixturePath);
        var decompressed = BarCompression.DecompressL33t(compressed);
        Assert.NotNull(decompressed);

        var scenario = new ScenarioFile(decompressed);
        var data = ScenarioPreviewData.TryBuild(scenario);
        Assert.NotNull(data);

        var index = new FileIndex();
        FileIndexBuilder.IndexBarFiles(index, [barPath]);
        Assert.True(index.Count > 0, "ArtTerrainTextures.bar yielded zero entries");

        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadEntry);

        int total = data!.TextureSet.Names.Count;
        Assert.True(total > 0, "Fixture has zero referenced textures");

        var unresolved = new List<string>();
        var resolved = new List<string>();
        foreach (var name in data.TextureSet.Names)
        {
            var bytes = await resolver.TryResolveAsync(name);
            if (bytes is null || bytes.Length == 0) unresolved.Add(name);
            else resolved.Add(name);
        }

        // Diagnostic: surface any names that didn't resolve so the suffix probing can be tightened
        var report = $"resolved={resolved.Count}/{total} unresolved=[{string.Join(", ", unresolved.Take(10))}]";
        Assert.True(unresolved.Count == 0, report);
    }

    [SkippableFact]
    public async Task FixtureNames_FullPipelineDecodesAllSlices()
    {
        Skip.IfNot(Directory.Exists(GamePath), "Game install not found");
        var barPath = Path.Combine(GamePath, "art", "ArtTerrainTextures.bar");
        Skip.IfNot(File.Exists(barPath), "ArtTerrainTextures.bar not found");

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "scenario-test1.mythscn");
        var scenario = new ScenarioFile(BarCompression.DecompressL33t(File.ReadAllBytes(fixturePath))!);
        var data = ScenarioPreviewData.TryBuild(scenario);
        Assert.NotNull(data);

        var index = new FileIndex();
        FileIndexBuilder.IndexBarFiles(index, [barPath]);
        var resolver = new ScenarioTextureLoader.NameResolver(index, ReadEntry);

        int uploaded = 0;
        Task UploadStub(int slice, byte[] rgba)
        {
            Assert.Equal(ScenarioTextureLoader.TargetSize * ScenarioTextureLoader.TargetSize * 4, rgba.Length);
            uploaded++;
            return Task.CompletedTask;
        }

        await ScenarioTextureLoader.LoadAllAsync(data!, resolver, UploadStub, onProgress: null, CancellationToken.None);

        var total = data!.TextureSet.Names.Count;
        Assert.Equal(total, uploaded);
        for (int i = 0; i < total; i++)
            Assert.True(data.SliceReady[i], $"slice {i} ({data.TextureSet.Names[i]}) was not marked ready");
    }
}
