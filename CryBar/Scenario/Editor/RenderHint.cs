namespace CryBar.Scenario.Editor;

/// <summary>
/// Bit flags that an <see cref="IScenarioCommand"/> publishes via <see cref="IScenarioCommand.Hint"/>
/// so the renderer knows which GPU buffers / mesh slices to rebuild after Apply/Undo.
/// </summary>
[System.Flags]
public enum RenderHint
{
    None             = 0,
    // Tile (group, sub) changed -> rebuild terrain mesh slice indices
    TerrainTexture   = 1 << 0,
    // Tile water type changed -> rebuild water mesh
    TerrainWater     = 1 << 1,
    // Vertex heights changed -> rebuild terrain verts + water mesh
    TerrainGeometry  = 1 << 2,
    // Entity added or removed -> rebuild billboard buffer
    EntityList       = 1 << 3,
    // Entity property changed in place
    EntityField      = 1 << 4,
}
