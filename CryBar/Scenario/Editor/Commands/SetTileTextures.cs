using System.Collections.Generic;
using System.Linq;

namespace CryBar.Scenario.Editor.Commands;

public sealed class SetTileTextures : IScenarioCommand
{
    readonly int[] _tileIdx;
    readonly byte _newGroup;
    readonly ushort _newSub;
    readonly byte[] _oldGroup;
    readonly ushort[] _oldSub;

    SetTileTextures(int[] tileIdx, byte newGroup, ushort newSub, byte[] oldGroup, ushort[] oldSub)
    {
        _tileIdx = tileIdx;
        _newGroup = newGroup; _newSub = newSub;
        _oldGroup = oldGroup; _oldSub = oldSub;
    }

    public string DisplayName => "Set tile texture";
    public RenderHint Hint => RenderHint.TerrainTexture;

    public static SetTileTextures? Create(ScenarioTerrain terrain, IReadOnlyList<int> tileIdx, byte newGroup, ushort newSub)
    {
        bool anyChange = false;
        var oldG = new byte[tileIdx.Count];
        var oldS = new ushort[tileIdx.Count];
        for (int i = 0; i < tileIdx.Count; i++)
        {
            int idx = tileIdx[i];
            oldG[i] = terrain.TileGroups[idx];
            oldS[i] = terrain.TileSubs[idx];
            if (oldG[i] != newGroup || oldS[i] != newSub) anyChange = true;
        }
        if (!anyChange) return null;
        return new SetTileTextures(tileIdx.ToArray(), newGroup, newSub, oldG, oldS);
    }

    public void Apply(ScenarioTerrain terrain, List<ScenarioEntity> _)
    {
        for (int i = 0; i < _tileIdx.Length; i++)
        {
            terrain.TileGroups[_tileIdx[i]] = _newGroup;
            terrain.TileSubs[_tileIdx[i]] = _newSub;
        }
    }

    public void Undo(ScenarioTerrain terrain, List<ScenarioEntity> _)
    {
        for (int i = 0; i < _tileIdx.Length; i++)
        {
            terrain.TileGroups[_tileIdx[i]] = _oldGroup[i];
            terrain.TileSubs[_tileIdx[i]] = _oldSub[i];
        }
    }
}
