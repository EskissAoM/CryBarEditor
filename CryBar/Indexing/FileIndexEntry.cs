namespace CryBar.Indexing;

public enum FileIndexSource : byte { RootFile, BarEntry }

public readonly struct FileIndexEntry
{
    public required string FullRelativePath { get; init; }
    public required FileIndexSource Source { get; init; }
    public string? BarFilePath { get; init; }
    public bool IsExternal { get; init; }

    /// <summary>
    /// Length of the BAR root path prefix (including separator) in FullRelativePath.
    /// EntryRelativePath is derived as FullRelativePath[BarRootPrefixLength..].
    /// Zero for root files or when the BAR has no root path.
    /// </summary>
    public ushort BarRootPrefixLength { get; init; }

    /// <summary>
    /// The BAR-relative path, derived from FullRelativePath by stripping the root prefix.
    /// Zero-allocation slice.
    /// </summary>
    public ReadOnlySpan<char> EntryRelativePath =>
        Source == FileIndexSource.BarEntry
            ? FullRelativePath.AsSpan(BarRootPrefixLength)
            : default;

    /// <summary>
    /// Derives filename from FullRelativePath. Zero-allocation slice.
    /// Accepts both '/' and '\\' as separators so game-format Windows paths
    /// resolve correctly when the host OS is Linux/macOS.
    /// </summary>
    public ReadOnlySpan<char> FileName
    {
        get
        {
            var span = FullRelativePath.AsSpan();
            int sep = span.LastIndexOfAny('/', '\\');
            return sep >= 0 ? span[(sep + 1)..] : span;
        }
    }
}
