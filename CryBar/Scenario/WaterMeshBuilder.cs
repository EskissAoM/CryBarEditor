namespace CryBar.Scenario;

public static class WaterMeshBuilder
{
    // Bias water level down so shore terrain wins the depth race (avoids the
    // green/blue flicker on co-planar surfaces). Larger than a tiny epsilon so
    // mip-noise and float rounding can't put water surface above ground level
    // mid-fragment. Visually water is 0.3 below the median data height -- small
    // on a 128-tile map but big enough to suppress flicker reliably.
    const float ZBias = 0.3f;

    public static WaterMesh? Build(ScenarioTerrain terrain)
    {
        var waterH = terrain.WaterHeights;
        if (waterH.Length == 0) return null;

        int mapX = terrain.MapSizeX;
        int mapZ = terrain.MapSizeZ;
        int vCols = mapX + 1;
        int vRows = mapZ + 1;
        if (waterH.Length != vCols * vRows) return null;

        // Compute median water level so the whole water mesh sits at one flat
        // height. Per-vertex water heights produce a surface that tilts down at
        // shore tiles where some corners are well below water, which slices into
        // the surrounding terrain and looks broken.
        float median = MedianPositive(waterH);
        if (median <= 0) return null;
        float waterY = median - ZBias;

        var vertexMap = new int[waterH.Length];
        Array.Fill(vertexMap, -1);

        var verts = new List<float>(64 * 3);
        var indices = new List<uint>(64);

        for (int tz = 0; tz < mapZ; tz++)
        for (int tx = 0; tx < mapX; tx++)
        {
            int v00 = tz       * vCols + tx;
            int v10 = tz       * vCols + (tx + 1);
            int v01 = (tz + 1) * vCols + tx;
            int v11 = (tz + 1) * vCols + (tx + 1);

            float h00 = waterH[v00];
            float h10 = waterH[v10];
            float h01 = waterH[v01];
            float h11 = waterH[v11];

            // Only emit tiles where all 4 corners have water -- "any corner" leaks
            // water onto neighbor tiles that just share a watered boundary vertex,
            // making the pond appear 1 tile wider than it actually is.
            if (h00 <= 0 || h10 <= 0 || h01 <= 0 || h11 <= 0) continue;

            int i00 = AddVertex(verts, vertexMap, v00, tx,     waterY, tz);
            int i10 = AddVertex(verts, vertexMap, v10, tx + 1, waterY, tz);
            int i01 = AddVertex(verts, vertexMap, v01, tx,     waterY, tz + 1);
            int i11 = AddVertex(verts, vertexMap, v11, tx + 1, waterY, tz + 1);

            indices.Add((uint)i00); indices.Add((uint)i10); indices.Add((uint)i01);
            indices.Add((uint)i10); indices.Add((uint)i11); indices.Add((uint)i01);
        }

        if (verts.Count == 0) return null;

        return new WaterMesh
        {
            Vertices = verts.ToArray(),
            Indices = indices.ToArray()
        };
    }

    static int AddVertex(List<float> verts, int[] vertexMap, int vIdx, int x, float y, int z)
    {
        if (vertexMap[vIdx] >= 0) return vertexMap[vIdx];
        int idx = verts.Count / 3;
        verts.Add(x);
        verts.Add(y);
        verts.Add(z);
        vertexMap[vIdx] = idx;
        return idx;
    }

    static float MedianPositive(float[] heights)
    {
        var pool = System.Buffers.ArrayPool<float>.Shared;
        var buf = pool.Rent(heights.Length);
        try
        {
            int count = 0;
            for (int i = 0; i < heights.Length; i++)
                if (heights[i] > 0) buf[count++] = heights[i];
            if (count == 0) return 0;
            Array.Sort(buf, 0, count);
            return buf[count / 2];
        }
        finally { pool.Return(buf); }
    }
}
