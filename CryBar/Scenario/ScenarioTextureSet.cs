namespace CryBar.Scenario;

public sealed class ScenarioTextureSet
{
    readonly List<string> _names;
    readonly Dictionary<(byte group, ushort sub), int> _indices;

    public IReadOnlyList<string> Names => _names;
    public IReadOnlyDictionary<(byte group, ushort sub), int> SliceIndices => _indices;

    ScenarioTextureSet(List<string> names, Dictionary<(byte, ushort), int> indices)
    {
        _names = names;
        _indices = indices;
    }

    /// <summary>
    /// Returns the slice index for (g, s); appends a new slot if not yet mapped.
    /// On append, sets <paramref name="addedSliceIndex"/> to the new index so the
    /// caller can drive a one-off DDT load + GL upload. Returns null in
    /// addedSliceIndex when (g, s) was already mapped (caller can no-op).
    ///
    /// Returns -1 (and addedSliceIndex = null) when textureName is the pseudo
    /// terrain "water" -- it's rendered via the WaterMesh + shader path, not
    /// from a DDT slice, so we skip allocating a slot for it.
    /// </summary>
    public int EnsureSlot(byte g, ushort s, string textureName, out int? addedSliceIndex)
    {
        if (IsPseudoWater(textureName))
        {
            addedSliceIndex = null;
            return -1;
        }
        if (_indices.TryGetValue((g, s), out var existing))
        {
            addedSliceIndex = null;
            return existing;
        }
        int newIdx = _names.Count;
        _indices[(g, s)] = newIdx;
        _names.Add(textureName);
        addedSliceIndex = newIdx;
        return newIdx;
    }

    /// <summary>
    /// Enumerates ONLY (g, s) pairs currently referenced by some tile in
    /// <c>terrain.TileGroups/TileSubs</c>. This is the working set the texture
    /// loader actually needs upfront -- typical scenarios use ~50 unique
    /// textures even when TerrainGroups declares hundreds. Pre-allocating slots
    /// for all declared (now ~480) thrashes the loader at scenario open.
    ///
    /// Picking a declared-but-currently-unused texture from the inspector
    /// triggers <see cref="EnsureSlot"/> at click time, which appends the
    /// missing slot and drives a one-off DDT load + GL upload via the host's
    /// grow path. Same code handles brand-new textures appended from the full
    /// game list.
    ///
    /// Order is the order of first encounter while walking tiles.
    ///
    /// The literal name "water" is excluded -- it's a pseudo-terrain rendered
    /// by the WaterMesh + shader path, not a DDT. Including it would yield a
    /// "1 missing" warning since no water_basecolor.ddt / water.ddt exists.
    /// </summary>
    public static ScenarioTextureSet Build(ScenarioTerrain terrain)
    {
        var names = new List<string>();
        var indices = new Dictionary<(byte, ushort), int>();

        var n = Math.Min(terrain.TileGroups.Length, terrain.TileSubs.Length);
        for (int i = 0; i < n; i++)
        {
            var g = terrain.TileGroups[i];
            var s = terrain.TileSubs[i];
            if (g >= terrain.TerrainGroups.Length) continue;
            var grp = terrain.TerrainGroups[g];
            if (s >= grp.Textures.Length) continue;

            var name = grp.Textures[s];
            if (IsPseudoWater(name)) continue;

            if (indices.TryAdd((g, s), names.Count))
                names.Add(name);
        }

        return new ScenarioTextureSet(names, indices);
    }

    static bool IsPseudoWater(string name)
        => string.Equals(name, "water", System.StringComparison.OrdinalIgnoreCase);
}
