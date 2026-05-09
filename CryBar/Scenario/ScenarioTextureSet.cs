namespace CryBar.Scenario;

public sealed class ScenarioTextureSet
{
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyDictionary<(byte group, ushort sub), int> SliceIndices { get; }

    ScenarioTextureSet(List<string> names, Dictionary<(byte, ushort), int> indices)
    {
        Names = names;
        SliceIndices = indices;
    }

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
            var group = terrain.TerrainGroups[g];
            if (s >= group.Textures.Length) continue;

            if (indices.TryAdd((g, s), names.Count))
                names.Add(group.Textures[s]);
        }

        return new ScenarioTextureSet(names, indices);
    }
}
