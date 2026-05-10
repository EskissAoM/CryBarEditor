using System.Collections.Generic;

namespace CryBar.Scenario.Editor;

public static class VertexHeightHelpers
{
    // Returns unique corner-vertex indices for the given tiles. Adjacent tiles
    // share corners; each shared corner is returned once.
    public static HashSet<int> UniqueCornerVertices(IEnumerable<int> tileIdx, int mapSizeX)
    {
        var rowStride = mapSizeX + 1;
        var set = new HashSet<int>();
        foreach (int idx in tileIdx)
        {
            int tx = idx % mapSizeX;
            int tz = idx / mapSizeX;
            set.Add(tz       * rowStride + tx);
            set.Add(tz       * rowStride + tx + 1);
            set.Add((tz + 1) * rowStride + tx);
            set.Add((tz + 1) * rowStride + tx + 1);
        }
        return set;
    }
}
