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

        // Water-tile mask. WaterType[t]==0 marks a water tile; any non-zero
        // value is a "no-water override" the editor uses to carve dry holes
        // out of low-lying terrain.
        var isWater = new bool[tileCount];
        for (int i = 0; i < tileCount && i < waterType.Length; i++)
            isWater[i] = waterType[i] == 0;

        // Flood-fill water tiles into connected components. A scenario can
        // have several water bodies at different elevations (e.g. a lake at
        // sea level plus a river up on a plateau); each one needs its own
        // surface y. A single global median snaps every body to one height
        // and buries the higher ones under terrain.
        var component = new int[tileCount];
        Array.Fill(component, -1);
        int compCount = AssignComponents(isWater, component, mapX, mapZ);
        if (compCount == 0) return null;

        // Per-component median water level over the vertices touching its
        // tiles. Median (vs mean) shrugs off shore vertices that dip below
        // the surface.
        var compY = new float[compCount];
        ComputeComponentLevels(component, waterH, compY, mapX, mapZ, vCols);

        var verts = new List<float>(64 * 3);
        var indices = new List<uint>(64);
        // Vertex map keyed by (component, vertex grid index): two abutting
        // bodies must not share a corner vertex because their y differs.
        var vertexMap = new Dictionary<long, int>();

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

        return new WaterMesh
        {
            Vertices = verts.ToArray(),
            Indices = indices.ToArray()
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

        // Collect each component's vertex set. HashSet dedupes the four
        // corners every tile contributes.
        var compVerts = new HashSet<int>[compCount];
        for (int i = 0; i < compCount; i++) compVerts[i] = new HashSet<int>();
        for (int t = 0; t < tileCount; t++)
        {
            int c = component[t];
            if (c < 0) continue;
            int tx = t % mapX, tz = t / mapX;
            var set = compVerts[c];
            set.Add(tz       * vCols + tx);
            set.Add(tz       * vCols + tx + 1);
            set.Add((tz + 1) * vCols + tx);
            set.Add((tz + 1) * vCols + tx + 1);
        }

        var pool = System.Buffers.ArrayPool<float>.Shared;
        for (int c = 0; c < compCount; c++)
        {
            var verts = compVerts[c];
            var buf = pool.Rent(verts.Count);
            try
            {
                int n = 0;
                foreach (int v in verts)
                    if (waterH[v] > 0) buf[n++] = waterH[v];
                if (n == 0) { compY[c] = float.NaN; continue; }
                Array.Sort(buf, 0, n);
                compY[c] = buf[n / 2] - ZBias;
            }
            finally { pool.Return(buf); }
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
