namespace CryBar.Scenario;

public static class TerrainMeshBuilder
{
    public static TerrainMesh Build(ScenarioTerrain terrain, ScenarioTextureSet textures)
    {
        int mapX = terrain.MapSizeX;
        int mapZ = terrain.MapSizeZ;
        int vCols = mapX + 1;
        int vRows = mapZ + 1;
        int vertexCount = vCols * vRows;

        var verts = new float[vertexCount * TerrainMesh.VertexStrideFloats];

        for (int vz = 0; vz < vRows; vz++)
        for (int vx = 0; vx < vCols; vx++)
        {
            int vIdx = vz * vCols + vx;
            int off = vIdx * TerrainMesh.VertexStrideFloats;

            float y = vIdx < terrain.Heights.Length ? terrain.Heights[vIdx] : 0f;
            verts[off + 0] = vx;
            verts[off + 1] = y;
            verts[off + 2] = vz;
            verts[off + 3] = 0f;
            verts[off + 4] = 0f;

            // Four neighbor tiles meeting at vertex (vx, vz)
            int sliceA = SliceAt(vx - 1, vz - 1, mapX, mapZ, terrain, textures);
            int sliceB = SliceAt(vx,     vz - 1, mapX, mapZ, terrain, textures);
            int sliceC = SliceAt(vx - 1, vz,     mapX, mapZ, terrain, textures);
            int sliceD = SliceAt(vx,     vz,     mapX, mapZ, terrain, textures);

            verts[off + 5] = sliceA;
            verts[off + 6] = sliceB;
            verts[off + 7] = sliceC;
            verts[off + 8] = sliceD;

            float wA = sliceA >= 0 ? 1f : 0f;
            float wB = sliceB >= 0 ? 1f : 0f;
            float wC = sliceC >= 0 ? 1f : 0f;
            float wD = sliceD >= 0 ? 1f : 0f;
            float sum = wA + wB + wC + wD;
            if (sum > 0)
            {
                wA /= sum; wB /= sum; wC /= sum; wD /= sum;
            }
            verts[off + 9]  = wA;
            verts[off + 10] = wB;
            verts[off + 11] = wC;
            // wD is implicit: 1 - (wA + wB + wC)
        }

        var indices = new uint[mapX * mapZ * 6];
        int ii = 0;
        for (int tz = 0; tz < mapZ; tz++)
        for (int tx = 0; tx < mapX; tx++)
        {
            uint v00 = (uint)(tz * vCols + tx);
            uint v10 = v00 + 1;
            uint v01 = v00 + (uint)vCols;
            uint v11 = v01 + 1;

            indices[ii++] = v00;
            indices[ii++] = v01;
            indices[ii++] = v10;

            indices[ii++] = v10;
            indices[ii++] = v01;
            indices[ii++] = v11;
        }

        return new TerrainMesh
        {
            MapSizeX = mapX,
            MapSizeZ = mapZ,
            Vertices = verts,
            Indices = indices
        };
    }

    static int SliceAt(int tx, int tz, int mapX, int mapZ, ScenarioTerrain terrain, ScenarioTextureSet textures)
    {
        if (tx < 0 || tz < 0 || tx >= mapX || tz >= mapZ) return -1;
        int tIdx = tz * mapX + tx;
        if (tIdx >= terrain.TileGroups.Length || tIdx >= terrain.TileSubs.Length) return -1;
        var key = (terrain.TileGroups[tIdx], terrain.TileSubs[tIdx]);
        return textures.SliceIndices.TryGetValue(key, out var slice) ? slice : -1;
    }
}
