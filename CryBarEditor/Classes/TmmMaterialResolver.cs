using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CryBar.Bar;
using CryBar.Export;
using CryBar.Indexing;
using CryBar.Utilities;

namespace CryBarEditor.Classes;

public sealed class TmmMaterialResolver
{
    public delegate ValueTask<PooledBuffer?> ReadEntryAsync(FileIndexEntry entry);

    public readonly struct ResolvedTextures
    {
        public required List<MaterialInfo> Materials { get; init; }
        public required Dictionary<string, (string FileName, byte[] DdtData)> Textures { get; init; }
    }

    readonly FileIndex _fileIndex;
    readonly ReadEntryAsync _readEntry;

    // Single-entry parse cache: probe-then-load is the dominant access pattern, so caching
    // the most recent (tmmFileName -> materials) avoids parsing the same XMB twice.
    string? _cachedMaterialsTmm;
    List<MaterialInfo>? _cachedMaterials;

    public TmmMaterialResolver(FileIndex fileIndex, ReadEntryAsync readEntry)
    {
        _fileIndex = fileIndex;
        _readEntry = readEntry;
    }

    /// <summary>
    /// Resolves the .material XML for a TMM and returns the parsed submaterials plus
    /// the raw decompressed DDT bytes for textures glTF can use (basecolor, normal map,
    /// Masks1 (ORM), and Masks2 (player-color mask)).
    /// Other texture roles are skipped to avoid wasted decompression and heap copies.
    /// Returns null if the material file can't be found or parsed.
    /// </summary>
    public async ValueTask<ResolvedTextures?> ResolveAsync(string tmmFileName, CancellationToken token = default)
    {
        try
        {
            var materials = await GetParsedMaterialsAsync(tmmFileName, token);
            if (materials == null) return null;

            var textures = new Dictionary<string, (string FileName, byte[] DdtData)>();

            foreach (var mat in materials)
            {
                foreach (var (texName, texPath) in mat.Textures)
                {
                    if (MaterialExporter.ClassifyRole(texName) is null)
                        continue;
                    if (textures.ContainsKey(texPath)) continue;

                    token.ThrowIfCancellationRequested();
                    var texFileName = Path.GetFileName(texPath.Replace('\\', '/'));

                    var texEntries = _fileIndex.Find(texFileName + ".ddt");
                    if (texEntries.Count == 0)
                        texEntries = _fileIndex.Find(texFileName);
                    if (texEntries.Count == 0) continue;

                    using var texData = await _readEntry(texEntries[0]);
                    if (texData == null) continue;

                    using var decompressedTex = BarCompression.EnsureDecompressedPooled(texData, out _);
                    textures[texPath] = (texFileName, decompressedTex.Span.ToArray());
                }
            }

            return new ResolvedTextures { Materials = materials, Textures = textures };
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cheap availability probe - parses just the material XML and tests basecolor/diffuse
    /// texture file presence in the FileIndex without decompressing any DDT bytes.
    /// </summary>
    public async ValueTask<bool> HasAtLeastBaseColorAsync(string tmmFileName, CancellationToken token = default)
    {
        try
        {
            var materials = await GetParsedMaterialsAsync(tmmFileName, token);
            if (materials == null) return false;

            foreach (var mat in materials)
            {
                foreach (var (texName, texPath) in mat.Textures)
                {
                    if (!MaterialExporter.IsBaseColorRole(texName)) continue;
                    var texFileName = Path.GetFileName(texPath.Replace('\\', '/'));
                    if (_fileIndex.Find(texFileName + ".ddt").Count > 0 ||
                        _fileIndex.Find(texFileName).Count > 0)
                        return true;
                }
            }
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    async ValueTask<List<MaterialInfo>?> GetParsedMaterialsAsync(string tmmFileName, CancellationToken token)
    {
        if (_cachedMaterialsTmm == tmmFileName && _cachedMaterials != null)
            return _cachedMaterials;

        var tmmName = Path.GetFileNameWithoutExtension(tmmFileName);
        var materialName = tmmName + ".material";

        var materialEntries = _fileIndex.Find(materialName + ".XMB");
        if (materialEntries.Count == 0)
            materialEntries = _fileIndex.Find(materialName);
        if (materialEntries.Count == 0) return null;

        var matEntry = materialEntries[0];
        using var matData = await _readEntry(matEntry);
        if (matData == null) return null;

        using var matBytes = BarCompression.EnsureDecompressedPooled(matData, out _);
        token.ThrowIfCancellationRequested();

        string? xmlText = matEntry.FileName.EndsWith(".XMB", StringComparison.OrdinalIgnoreCase)
            ? ConversionHelper.ConvertXmbToXmlText(matBytes.Span)
            : Encoding.UTF8.GetString(matBytes.Span);

        if (xmlText == null) return null;

        var parsed = MaterialExporter.ParseMaterialXml(xmlText);
        _cachedMaterialsTmm = tmmFileName;
        _cachedMaterials = parsed;
        return parsed;
    }
}
