using System.Collections.Generic;
using System.Linq;

namespace CryBar.Scenario.Editor.Commands;

public sealed class SetVertexHeights : IScenarioCommand
{
    readonly int[] _vertexIdx;
    readonly float[] _newHeights;
    readonly float[] _oldHeights;

    SetVertexHeights(int[] vertexIdx, float[] newHeights, float[] oldHeights)
    {
        _vertexIdx = vertexIdx; _newHeights = newHeights; _oldHeights = oldHeights;
    }

    public string DisplayName => "Set vertex heights";
    public RenderHint Hint => RenderHint.TerrainGeometry;

    public static SetVertexHeights? Create(ScenarioTerrain terrain, IReadOnlyList<int> vertexIdx, IReadOnlyList<float> newHeights)
    {
        if (vertexIdx.Count != newHeights.Count)
            throw new System.ArgumentException("vertexIdx and newHeights must have equal counts");

        bool anyChange = false;
        var old = new float[vertexIdx.Count];
        var nu  = new float[vertexIdx.Count];
        for (int i = 0; i < vertexIdx.Count; i++)
        {
            old[i] = terrain.Heights[vertexIdx[i]];
            nu[i]  = newHeights[i];
            if (old[i] != nu[i]) anyChange = true;
        }
        if (!anyChange) return null;
        return new SetVertexHeights(vertexIdx.ToArray(), nu, old);
    }

    public void Apply(ScenarioTerrain terrain, List<ScenarioEntity> _)
    {
        for (int i = 0; i < _vertexIdx.Length; i++)
            terrain.Heights[_vertexIdx[i]] = _newHeights[i];
    }

    public void Undo(ScenarioTerrain terrain, List<ScenarioEntity> _)
    {
        for (int i = 0; i < _vertexIdx.Length; i++)
            terrain.Heights[_vertexIdx[i]] = _oldHeights[i];
    }
}
