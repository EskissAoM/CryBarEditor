using System.Collections.Generic;
using System.Linq;

namespace CryBar.Scenario.Editor.Commands;

public sealed class DeleteEntities : IScenarioCommand
{
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
        return new DeleteEntities(removed.ToArray());
    }

    public void Apply(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        // Remove highest-index-first so earlier indices stay valid
        var ordered = _removed.OrderByDescending(r => r.OriginalIndex);
        foreach (var r in ordered) entities.RemoveAt(r.OriginalIndex);
    }

    public void Undo(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        // Re-insert lowest-index-first so target indices align
        var ordered = _removed.OrderBy(r => r.OriginalIndex);
        foreach (var r in ordered) entities.Insert(r.OriginalIndex, r.Snapshot);
    }
}
