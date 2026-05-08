namespace CryBar.Scenario;

public static class WaterMeshBuilder
{
    public static WaterMesh? Build(ScenarioTerrain terrain)
    {
        if (terrain.WaterHeights.Length == 0) return null;

        var sorted = terrain.WaterHeights.Where(h => h > 0).OrderBy(h => h).ToArray();
        if (sorted.Length == 0) return null;

        float median = sorted[sorted.Length / 2];

        return new WaterMesh
        {
            MapSizeX = terrain.MapSizeX,
            MapSizeZ = terrain.MapSizeZ,
            Height = median
        };
    }
}
