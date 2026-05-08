using System.Buffers;

namespace CryBar.Scenario;

public static class WaterMeshBuilder
{
    public static WaterMesh? Build(ScenarioTerrain terrain)
    {
        var heights = terrain.WaterHeights;
        if (heights.Length == 0) return null;

        var pool = ArrayPool<float>.Shared;
        var buf = pool.Rent(heights.Length);
        try
        {
            int count = 0;
            for (int i = 0; i < heights.Length; i++)
            {
                var h = heights[i];
                if (h > 0) buf[count++] = h;
            }
            if (count == 0) return null;

            Array.Sort(buf, 0, count);
            float median = buf[count / 2];

            return new WaterMesh
            {
                MapSizeX = terrain.MapSizeX,
                MapSizeZ = terrain.MapSizeZ,
                Height = median
            };
        }
        finally
        {
            pool.Return(buf);
        }
    }
}
