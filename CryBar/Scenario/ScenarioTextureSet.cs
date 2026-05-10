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

    /// Returns the slice for (g, s); appends a new slot if not yet mapped.
    /// addedSliceIndex is the new index when appended, null otherwise.
    /// Returns -1 for "water" (rendered via WaterMesh + shader, not a DDT slice).
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

    /// Enumerates only (g, s) pairs referenced by some tile -- pre-allocating
    /// slots for all declared (~480) thrashes the loader at scenario open.
    /// EnsureSlot lazily appends declared-but-unused or brand-new picks.
    /// "water" is excluded (pseudo-terrain rendered via WaterMesh).
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
