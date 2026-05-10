using System.Collections.Generic;
using System.Linq;

namespace CryBar.Scenario.Editor.Commands;

public sealed class SetEntityPlayers : IScenarioCommand
{
    readonly uint[] _ids;
    readonly byte _newPlayer;
    readonly byte[] _old;

    SetEntityPlayers(uint[] ids, byte newPlayer, byte[] old)
    { _ids = ids; _newPlayer = newPlayer; _old = old; }

    public string DisplayName => "Set entity player";
    public RenderHint Hint => RenderHint.EntityField;

    public static SetEntityPlayers? Create(IReadOnlyList<ScenarioEntity> entities, IReadOnlyList<uint> ids, byte newPlayer)
    {
        var idToIndex = new Dictionary<uint, int>(entities.Count);
        for (int i = 0; i < entities.Count; i++) idToIndex[entities[i].EntityId] = i;

        bool anyChange = false;
        var old = new byte[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            old[i] = entities[idToIndex[ids[i]]].PlayerId;
            if (old[i] != newPlayer) anyChange = true;
        }
        if (!anyChange) return null;
        return new SetEntityPlayers(ids.ToArray(), newPlayer, old);
    }

    public void Apply(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = new Dictionary<uint, int>(entities.Count);
        for (int i = 0; i < entities.Count; i++) idToIndex[entities[i].EntityId] = i;
        foreach (var id in _ids)
            entities[idToIndex[id]].PlayerId = _newPlayer;
    }

    public void Undo(ScenarioTerrain _, List<ScenarioEntity> entities)
    {
        var idToIndex = new Dictionary<uint, int>(entities.Count);
        for (int i = 0; i < entities.Count; i++) idToIndex[entities[i].EntityId] = i;
        for (int i = 0; i < _ids.Length; i++)
            entities[idToIndex[_ids[i]]].PlayerId = _old[i];
    }
}
