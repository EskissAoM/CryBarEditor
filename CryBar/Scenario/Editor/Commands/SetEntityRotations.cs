using System.Collections.Generic;
using System.Linq;

namespace CryBar.Scenario.Editor.Commands;

public sealed class SetEntityRotations : IScenarioCommand
{
    readonly uint[] _ids;
    readonly Matrix3x3[] _new;
    readonly Matrix3x3[] _old;

    SetEntityRotations(uint[] ids, Matrix3x3[] nu, Matrix3x3[] old)
    { _ids = ids; _new = nu; _old = old; }

    public string DisplayName => "Set entity rotation";
    public RenderHint Hint => RenderHint.EntityField;

    public static SetEntityRotations? Create(IReadOnlyList<ScenarioEntity> entities, IReadOnlyList<uint> ids, IReadOnlyList<Matrix3x3> newRotations)
    {
        if (ids.Count != newRotations.Count)
            throw new System.ArgumentException("ids and newRotations must have equal counts");

        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        bool anyChange = false;
        var old = new Matrix3x3[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            old[i] = entities[idToIndex[ids[i]]].Rotation;
            if (old[i] != newRotations[i]) anyChange = true;
        }
        if (!anyChange) return null;
        return new SetEntityRotations(ids.ToArray(), newRotations.ToArray(), old);
    }

    public void Apply(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        for (int i = 0; i < _ids.Length; i++)
            entities[idToIndex[_ids[i]]].Rotation = _new[i];
    }

    public void Undo(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        for (int i = 0; i < _ids.Length; i++)
            entities[idToIndex[_ids[i]]].Rotation = _old[i];
    }
}
