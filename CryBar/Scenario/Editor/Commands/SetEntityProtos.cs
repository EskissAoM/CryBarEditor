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

    /// <summary>
    /// Resolves <paramref name="newProtoName"/> against the scenario's TM table.
    /// If the name isn't there, appends it -- the new index is the previous
    /// table count. The protoTable is mutated in place so subsequent commands
    /// (and the eventual save flush) see the updated table.
    ///
    /// Append-only: Undo restores per-entity Old indices/names but never pops
    /// the appended TM entry, since later commands may have come to rely on it.
    /// Orphan TM entries are harmless on disk.
    /// </summary>
    public static SetEntityProtos? Create(
        IReadOnlyList<ScenarioEntity> entities,
        IReadOnlyList<uint> ids,
        string newProtoName,
        List<string> protoTable)
    {
        // Resolve the index in the scenario TM table; append if missing.
        int newProtoIndex = protoTable.IndexOf(newProtoName);
        bool appended = false;
        if (newProtoIndex < 0)
        {
            newProtoIndex = protoTable.Count;
            protoTable.Add(newProtoName);
            appended = true;
        }

        bool anyChange = appended; // if we added to the table, that's a change
        var oldIdx  = new int[ids.Count];
        var oldName = new string[ids.Count];
        var idToIndex = BuildLookup(entities);
        for (int i = 0; i < ids.Count; i++)
        {
            var e = entities[idToIndex[ids[i]]];
            oldIdx[i]  = e.ProtoIndex;
            oldName[i] = e.ProtoName;
            if (oldIdx[i] != newProtoIndex || oldName[i] != newProtoName) anyChange = true;
        }

        // Even when nothing changed for the selected entities, an append still
        // counts as a real edit; we keep the command so Undo can revert any
        // entity reassignments. Pure no-op (no append + no entity diff) returns null.
        if (!anyChange) return null;
        return new SetEntityProtos(ids.ToArray(), newProtoIndex, newProtoName, oldIdx, oldName);
    }

    public void Apply(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = BuildLookup(entities);
        foreach (var id in _ids)
        {
            var e = entities[idToIndex[id]];
            e.ProtoIndex = _newProtoIndex;
            e.ProtoName  = _newProtoName;
        }
    }

    public void Undo(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = BuildLookup(entities);
        for (int i = 0; i < _ids.Length; i++)
        {
            var e = entities[idToIndex[_ids[i]]];
            e.ProtoIndex = _oldProtoIndex[i];
            e.ProtoName  = _oldProtoName[i];
        }
        // Append-only: do NOT pop the TM entry on undo. See Create() docs.
    }

    static Dictionary<uint, int> BuildLookup(IReadOnlyList<ScenarioEntity> entities)
    {
        var d = new Dictionary<uint, int>(entities.Count);
        for (int i = 0; i < entities.Count; i++) d[entities[i].EntityId] = i;
        return d;
    }
}
