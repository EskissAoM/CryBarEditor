using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CryBarEditor.Classes;

public sealed class ProtoEditorMetadataCacheEntry
{
    public int Version { get; set; }
    public string? DataBarPath { get; set; }
    public long DataBarSize { get; set; }
    public long DataBarLastWriteUtcTicks { get; set; }
    public string? TacticsDirectoryPath { get; set; }
    public ulong TacticsDirectoryFingerprint { get; set; }
    public Dictionary<string, string> GlobalTacticsActionTypes { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProtoEditorMetadataCacheEntry))]
internal partial class ProtoEditorMetadataCacheJsonContext : JsonSerializerContext { }

public static class ProtoEditorMetadataCacheStore
{
    private const string CacheFilename = "aom_editor_proto_metadata_cache.json";
    private const int CurrentVersion = 1;

    public static Dictionary<string, string>? LoadGlobalTacticsActionTypes(string? dataBarPath, string? gameplayDirectory)
    {
        var cachePath = ProtoEditorSettings.GetAppDataPath(CacheFilename);
        if (!File.Exists(cachePath))
            return null;

        try
        {
            var json = File.ReadAllText(cachePath);
            var entry = JsonSerializer.Deserialize(json, ProtoEditorMetadataCacheJsonContext.Default.ProtoEditorMetadataCacheEntry);
            if (entry == null || entry.Version != CurrentVersion)
                return null;

            if (!MatchesDataBar(entry, dataBarPath) || !MatchesTacticsDirectory(entry, gameplayDirectory))
                return null;

            return entry.GlobalTacticsActionTypes
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                .ToDictionary(kvp => kvp.Key.Trim(), kvp => kvp.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveGlobalTacticsActionTypes(string? dataBarPath, string? gameplayDirectory, IReadOnlyDictionary<string, string> globalTacticsActionTypes)
    {
        try
        {
            var cachePath = ProtoEditorSettings.GetAppDataPath(CacheFilename);
            var tacticsDirectory = ResolveTacticsDirectory(gameplayDirectory);
            var entry = new ProtoEditorMetadataCacheEntry
            {
                Version = CurrentVersion,
                DataBarPath = NormalizePath(dataBarPath),
                DataBarSize = TryGetFileSize(dataBarPath),
                DataBarLastWriteUtcTicks = TryGetLastWriteTicks(dataBarPath),
                TacticsDirectoryPath = NormalizePath(tacticsDirectory),
                TacticsDirectoryFingerprint = ComputeDirectoryFingerprint(tacticsDirectory),
                GlobalTacticsActionTypes = globalTacticsActionTypes
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    .ToDictionary(kvp => kvp.Key.Trim(), kvp => kvp.Value.Trim(), StringComparer.OrdinalIgnoreCase),
            };

            var json = JsonSerializer.Serialize(entry, ProtoEditorMetadataCacheJsonContext.Default.ProtoEditorMetadataCacheEntry);
            File.WriteAllText(cachePath, json);
        }
        catch
        {
            // Ignore cache persistence failures and fall back to live scanning next time.
        }
    }

    private static bool MatchesDataBar(ProtoEditorMetadataCacheEntry entry, string? dataBarPath)
    {
        if (!TryGetFileInfo(dataBarPath, out var fullPath, out var size, out var lastWriteTicks))
            return false;

        return string.Equals(entry.DataBarPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
               entry.DataBarSize == size &&
               entry.DataBarLastWriteUtcTicks == lastWriteTicks;
    }

    private static bool MatchesTacticsDirectory(ProtoEditorMetadataCacheEntry entry, string? gameplayDirectory)
    {
        var tacticsDirectory = ResolveTacticsDirectory(gameplayDirectory);
        var normalizedPath = NormalizePath(tacticsDirectory);
        if (!string.Equals(entry.TacticsDirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            return false;

        return entry.TacticsDirectoryFingerprint == ComputeDirectoryFingerprint(tacticsDirectory);
    }

    private static string? ResolveTacticsDirectory(string? gameplayDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameplayDirectory))
            return null;

        var tacticsDirectory = Path.Combine(gameplayDirectory, "tactics");
        return Directory.Exists(tacticsDirectory) ? tacticsDirectory : null;
    }

    private static bool TryGetFileInfo(string? path, out string? fullPath, out long size, out long lastWriteTicks)
    {
        fullPath = NormalizePath(path);
        size = 0;
        lastWriteTicks = 0;

        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            return false;

        var fileInfo = new FileInfo(fullPath);
        size = fileInfo.Length;
        lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
        return true;
    }

    private static long TryGetFileSize(string? path)
        => TryGetFileInfo(path, out _, out var size, out _) ? size : 0;

    private static long TryGetLastWriteTicks(string? path)
        => TryGetFileInfo(path, out _, out _, out var lastWriteTicks) ? lastWriteTicks : 0;

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static ulong ComputeDirectoryFingerprint(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return 0;

        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;

        void AddString(string value)
        {
            foreach (var character in value)
            {
                hash ^= character;
                hash *= prime;
            }
        }

        void AddLong(long value)
        {
            unchecked
            {
                for (int i = 0; i < sizeof(long); i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= prime;
                }
            }
        }

        foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                     .Where(IsRelevantTacticsFile)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedPath = NormalizePath(path) ?? path;
            var relativePath = Path.GetRelativePath(directoryPath, normalizedPath);
            var fileInfo = new FileInfo(normalizedPath);
            AddString(relativePath.ToUpperInvariant());
            AddLong(fileInfo.Length);
            AddLong(fileInfo.LastWriteTimeUtc.Ticks);
        }

        return hash;
    }

    private static bool IsRelevantTacticsFile(string path)
    {
        var extension = Path.GetExtension(path);
        var name = Path.GetFileName(path);
        return name.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tactics.xmb", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xmb", StringComparison.OrdinalIgnoreCase);
    }
}
