namespace CryBar.Scenario;

public static class WaterMeshBuilder
{
    // Bias water level down so shore terrain wins the depth race (avoids the
    // green/blue flicker on co-planar surfaces). The renderer scales Y by ~0.5
    // for visual parity with the in-game camera, so this raw-unit bias is
    // roughly 2x what the viewport "feels". 0.3 here keeps water sitting
    // close to its true level while still nudging it under shore vertices.
    internal const float ZBias = 0.3f;

    public static WaterMesh? Build(ScenarioTerrain terrain)
    {
        var waterH = terrain.WaterHeights;
        if (waterH.Length == 0) return null;

        int mapX = terrain.MapSizeX;
        int mapZ = terrain.MapSizeZ;
        int vCols = mapX + 1;
        int vRows = mapZ + 1;
        if (waterH.Length != vCols * vRows) return null;

        int tileCount = mapX * mapZ;
        var waterType = terrain.WaterType;

        // WaterType[t] is an index into WaterNames; the byte value 255 is the
        // "no water" sentinel. Any other value (0 for the default body,
        // 13/14/16/etc for variants) means the tile holds water.
        var isWater = new bool[tileCount];
        int waterTiles = 0;
        for (int i = 0; i < tileCount && i < waterType.Length; i++)
        {
            if (waterType[i] != 255) { isWater[i] = true; waterTiles++; }
        }
        if (waterTiles == 0) return null;

        // Flood-fill water tiles into connected components. A scenario can
        // have several water bodies at different elevations (e.g. a lake at
        // sea level plus a river up on a plateau); each one needs its own
        // surface y. A single global median snaps every body to one height
        // and buries the higher ones under terrain.
        var component = new int[tileCount];
        Array.Fill(component, -1);
        int compCount = AssignComponents(isWater, component, mapX, mapZ);
        if (compCount == 0) return null;

        var compY = new float[compCount];
        ComputeComponentLevels(component, waterH, compY, mapX, mapZ, vCols);

        // Worst-case sizing: every water tile contributes 4 verts / 6 indices
        // (no sharing between components; sharing inside a component shrinks it).
        var verts = new List<float>(waterTiles * 4 * 3);
        var indices = new List<uint>(waterTiles * 6);
        // Vertex map keyed by (component, vertex grid index): two abutting
        // bodies must not share a corner vertex because their y differs.
        var vertexMap = new Dictionary<long, int>(waterTiles * 4);

        for (int tz = 0; tz < mapZ; tz++)
        for (int tx = 0; tx < mapX; tx++)
        {
            int tIdx = tz * mapX + tx;
            int c = component[tIdx];
            if (c < 0) continue;
            float y = compY[c];
            if (float.IsNaN(y)) continue;

            int v00 = tz       * vCols + tx;
            int v10 = tz       * vCols + (tx + 1);
            int v01 = (tz + 1) * vCols + tx;
            int v11 = (tz + 1) * vCols + (tx + 1);

            int i00 = AddVertex(verts, vertexMap, c, v00, tx,     y, tz);
            int i10 = AddVertex(verts, vertexMap, c, v10, tx + 1, y, tz);
            int i01 = AddVertex(verts, vertexMap, c, v01, tx,     y, tz + 1);
            int i11 = AddVertex(verts, vertexMap, c, v11, tx + 1, y, tz + 1);

            indices.Add((uint)i00); indices.Add((uint)i10); indices.Add((uint)i01);
            indices.Add((uint)i10); indices.Add((uint)i11); indices.Add((uint)i01);
        }

        if (verts.Count == 0) return null;

        var tileWaterY = new float[tileCount];
        Array.Fill(tileWaterY, float.NaN);
        for (int t = 0; t < tileCount; t++)
        {
            int c = component[t];
            if (c >= 0) tileWaterY[t] = compY[c];
        }

        return new WaterMesh
        {
            Vertices = verts.ToArray(),
            Indices = indices.ToArray(),
            TileWaterY = tileWaterY,
            MapX = mapX,
            MapZ = mapZ,
        };
    }

    static int AssignComponents(bool[] isWater, int[] component, int mapX, int mapZ)
    {
        int tileCount = mapX * mapZ;
        int compCount = 0;
        var stack = new Stack<int>();
        for (int start = 0; start < tileCount; start++)
        {
            if (!isWater[start] || component[start] >= 0) continue;
            component[start] = compCount;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                int tx = t % mapX;
                int tz = t / mapX;
                if (tx > 0)        TryEnqueue(t - 1,    isWater, component, compCount, stack);
                if (tx < mapX - 1) TryEnqueue(t + 1,    isWater, component, compCount, stack);
                if (tz > 0)        TryEnqueue(t - mapX, isWater, component, compCount, stack);
                if (tz < mapZ - 1) TryEnqueue(t + mapX, isWater, component, compCount, stack);
            }
            compCount++;
        }
        return compCount;
    }

    static void TryEnqueue(int t, bool[] isWater, int[] component, int compId, Stack<int> stack)
    {
        if (!isWater[t] || component[t] >= 0) return;
        component[t] = compId;
        stack.Push(t);
    }

    static void ComputeComponentLevels(int[] component, float[] waterH, float[] compY,
                                       int mapX, int mapZ, int vCols)
    {
        int tileCount = mapX * mapZ;
        int compCount = compY.Length;

        // Tile count per component sizes each per-component sample buffer
        // (worst case 4 corners per tile).
        var tilesPerComp = new int[compCount];
        for (int t = 0; t < tileCount; t++)
        {
            int c = component[t];
            if (c >= 0) tilesPerComp[c]++;
        }

        // Pooled per-component sample arrays sized at 4 samples per tile (one
        // per corner). Sampling without dedupe over-weights interior vertices
        // (4 samples vs 1 at the shore corner) -- which is what we want, since
        // shore corners can dip below the true surface and interior is
        // authoritative.
        var pool = System.Buffers.ArrayPool<float>.Shared;
        var samples = new float[compCount][];
        var sampleCounts = new int[compCount];
        for (int c = 0; c < compCount; c++)
            samples[c] = pool.Rent(tilesPerComp[c] * 4);

        try
        {
            for (int t = 0; t < tileCount; t++)
            {
                int c = component[t];
                if (c < 0) continue;
                int tx = t % mapX, tz = t / mapX;
                var arr = samples[c];
                int n = sampleCounts[c];
                float h00 = waterH[tz       * vCols + tx];
                float h10 = waterH[tz       * vCols + tx + 1];
                float h01 = waterH[(tz + 1) * vCols + tx];
                float h11 = waterH[(tz + 1) * vCols + tx + 1];
                arr[n++] = h00;
                arr[n++] = h10;
                arr[n++] = h01;
                arr[n++] = h11;
                sampleCounts[c] = n;
            }

            for (int c = 0; c < compCount; c++)
            {
                int n = sampleCounts[c];
                if (n == 0) { compY[c] = float.NaN; continue; }
                Array.Sort(samples[c], 0, n);
                compY[c] = samples[c][n / 2] - ZBias;
            }
        }
        finally
        {
            for (int c = 0; c < compCount; c++) pool.Return(samples[c]);
        }
    }

    static int AddVertex(List<float> verts, Dictionary<long, int> vertexMap,
                         int comp, int vIdx, int x, float y, int z)
    {
        long key = ((long)comp << 32) | (uint)vIdx;
        if (vertexMap.TryGetValue(key, out int idx)) return idx;
        idx = verts.Count / 3;
        verts.Add(x);
        verts.Add(y);
        verts.Add(z);
        vertexMap[key] = idx;
        return idx;
    }
}
