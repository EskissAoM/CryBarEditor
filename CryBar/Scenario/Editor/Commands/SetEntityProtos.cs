using System.Collections.Generic;
using System.Linq;

namespace CryBar.Scenario.Editor.Commands;

public sealed class SetEntityProtos : IScenarioCommand
{
    readonly uint[] _ids;
    readonly int _newProtoIndex;
    readonly string _newProtoName;
    readonly int[] _oldProtoIndex;
    readonly string[] _oldProtoName;

    SetEntityProtos(uint[] ids, int newIdx, string newName, int[] oldIdx, string[] oldName)
    {
        _ids = ids; _newProtoIndex = newIdx; _newProtoName = newName;
        _oldProtoIndex = oldIdx; _oldProtoName = oldName;
    }

    public string DisplayName => "Set entity proto";
    public RenderHint Hint => RenderHint.EntityField;

    /// Resolves newProtoName against the scenario's TM table; appends if missing.
    /// Append-only: Undo never pops -- later commands may reference the new entry,
    /// and orphan TM entries are harmless on disk.
    public static SetEntityProtos? Create(
        IReadOnlyList<ScenarioEntity> entities,
        IReadOnlyList<uint> ids,
        string newProtoName,
        List<string> protoTable)
    {
        if (ids.Count == 0) return null;

        int newProtoIndex = protoTable.IndexOf(newProtoName);
        bool appended = false;
        if (newProtoIndex < 0)
        {
            newProtoIndex = protoTable.Count;
            protoTable.Add(newProtoName);
            appended = true;
        }

        bool anyChange = appended;
        var oldIdx  = new int[ids.Count];
        var oldName = new string[ids.Count];
        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        for (int i = 0; i < ids.Count; i++)
        {
            var e = entities[idToIndex[ids[i]]];
            oldIdx[i]  = e.ProtoIndex;
            oldName[i] = e.ProtoName;
            if (oldIdx[i] != newProtoIndex || oldName[i] != newProtoName) anyChange = true;
        }
        if (!anyChange) return null;
        return new SetEntityProtos(ids.ToArray(), newProtoIndex, newProtoName, oldIdx, oldName);
    }

    public void Apply(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        foreach (var id in _ids)
        {
            var e = entities[idToIndex[id]];
            e.ProtoIndex = _newProtoIndex;
            e.ProtoName  = _newProtoName;
        }
    }

    public void Undo(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = CommandHelpers.BuildIdToIndex(entities);
        for (int i = 0; i < _ids.Length; i++)
        {
            var e = entities[idToIndex[_ids[i]]];
            e.ProtoIndex = _oldProtoIndex[i];
            e.ProtoName  = _oldProtoName[i];
        }
    }
}
