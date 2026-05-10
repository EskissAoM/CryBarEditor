using System.Collections.Generic;
using System.Linq;

namespace CryBar.Scenario.Editor.Commands;

public sealed class SetTileWaterTypes : IScenarioCommand
{
    readonly int[] _tileIdx;
    readonly byte _newValue;
    readonly byte[] _old;

    SetTileWaterTypes(int[] tileIdx, byte newValue, byte[] old)
    {
        _tileIdx = tileIdx; _newValue = newValue; _old = old;
    }

    public string DisplayName => "Set tile water type";
    public RenderHint Hint => RenderHint.TerrainWater;

    public static SetTileWaterTypes? Create(ScenarioTerrain terrain, IReadOnlyList<int> tileIdx, byte newValue)
    {
        bool anyChange = false;
        var old = new byte[tileIdx.Count];
        for (int i = 0; i < tileIdx.Count; i++)
        {
            old[i] = terrain.WaterType[tileIdx[i]];
            if (old[i] != newValue) anyChange = true;
        }
        if (!anyChange) return null;
        return new SetTileWaterTypes(tileIdx.ToArray(), newValue, old);
    }

    public void Apply(ScenarioTerrain terrain, List<ScenarioEntity> _)
    {
        for (int i = 0; i < _tileIdx.Length; i++)
            terrain.WaterType[_tileIdx[i]] = _newValue;
    }

    public void Undo(ScenarioTerrain terrain, List<ScenarioEntity> _)
    {
        for (int i = 0; i < _tileIdx.Length; i++)
            terrain.WaterType[_tileIdx[i]] = _old[i];
    }
}
