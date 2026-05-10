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
    /// Enumerates ALL (g, s) pairs declared in <c>terrain.TerrainGroups</c>, not
    /// just those currently referenced by tiles. Pre-allocating slots for every
    /// declared texture means the renderer can switch a tile to any in-scenario
    /// texture without ending up with slice = -1 (which renders as placeholder).
    /// Order is deterministic: group-major, sub-minor.
    ///
    /// The literal name "water" is excluded -- it's a pseudo-terrain rendered by
    /// the WaterMesh + shader path, not a DDT. Including it would yield a permanent
    /// "1 missing" warning in the inspector since no water_basecolor.ddt / water.ddt
    /// exists. Skipped slots leave the (g, s) -> slice mapping ABSENT, which makes
    /// TerrainMeshBuilder return slice = -1 for water tiles -- expected and fine.
    /// </summary>
    public static ScenarioTextureSet Build(ScenarioTerrain terrain)
    {
        var names = new List<string>();
        var indices = new Dictionary<(byte, ushort), int>();

        for (int gi = 0; gi < terrain.TerrainGroups.Length; gi++)
        {
            byte g = (byte)gi;
            var grp = terrain.TerrainGroups[gi];
            for (int si = 0; si < grp.Textures.Length; si++)
            {
                ushort s = (ushort)si;
                if (IsPseudoWater(grp.Textures[si])) continue;
                if (indices.TryAdd((g, s), names.Count))
                    names.Add(grp.Textures[si]);
            }
        }

        return new ScenarioTextureSet(names, indices);
    }

    static bool IsPseudoWater(string name)
        => string.Equals(name, "water", System.StringComparison.OrdinalIgnoreCase);
}
