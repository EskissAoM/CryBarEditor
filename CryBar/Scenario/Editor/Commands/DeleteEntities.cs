using System.Collections.Generic;

namespace CryBar.Scenario.Editor.Commands;

public sealed class DeleteEntities : IScenarioCommand
{
    // Sorted by OriginalIndex ascending; Apply walks backward, Undo forward.
    readonly (int OriginalIndex, ScenarioEntity Snapshot)[] _removed;

    DeleteEntities((int, ScenarioEntity)[] removed) { _removed = removed; }

    public string DisplayName => "Delete entities";
    public RenderHint Hint => RenderHint.EntityList;

    public static DeleteEntities? Create(IReadOnlyList<ScenarioEntity> entities, IReadOnlyList<uint> ids)
    {
        if (ids.Count == 0) return null;

        var idSet = new HashSet<uint>(ids);
        var removed = new List<(int, ScenarioEntity)>(ids.Count);
        for (int i = 0; i < entities.Count; i++)
        {
            if (idSet.Contains(entities[i].EntityId))
                removed.Add((i, entities[i]));
        }
        if (removed.Count == 0) return null;
        removed.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return new DeleteEntities(removed.ToArray());
    }

    public void Apply(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        for (int i = _removed.Length - 1; i >= 0; i--)
            entities.RemoveAt(_removed[i].OriginalIndex);
    }

    public void Undo(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        for (int i = 0; i < _removed.Length; i++)
            entities.Insert(_removed[i].OriginalIndex, _removed[i].Snapshot);
    }
}
