using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CryBar.Scenario.Editor.Commands;

public sealed class SetEntityPositions : IScenarioCommand
{
    readonly uint[] _ids;
    readonly Vector3[] _new;
    readonly Vector3[] _old;

    SetEntityPositions(uint[] ids, Vector3[] nu, Vector3[] old)
    { _ids = ids; _new = nu; _old = old; }

    public string DisplayName => "Set entity position";
    public RenderHint Hint => RenderHint.EntityField;

    public static SetEntityPositions? Create(IReadOnlyList<ScenarioEntity> entities, IReadOnlyList<uint> ids, IReadOnlyList<Vector3> newPositions)
    {
        if (ids.Count != newPositions.Count)
            throw new System.ArgumentException("ids and newPositions must have equal counts");

        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        bool anyChange = false;
        var old = new Vector3[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            old[i] = entities[idToIndex[ids[i]]].Position;
            if (old[i] != newPositions[i]) anyChange = true;
        }
        if (!anyChange) return null;
        return new SetEntityPositions(ids.ToArray(), newPositions.ToArray(), old);
    }

    public void Apply(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        for (int i = 0; i < _ids.Length; i++)
            entities[idToIndex[_ids[i]]].Position = _new[i];
    }

    public void Undo(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        for (int i = 0; i < _ids.Length; i++)
            entities[idToIndex[_ids[i]]].Position = _old[i];
    }
}
