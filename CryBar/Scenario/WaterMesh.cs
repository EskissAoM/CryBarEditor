namespace CryBar.Scenario;

public sealed class WaterMesh
{
    public required float[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    // Per-tile water surface Y in raw scenario units (NaN = no water on that tile).
    // Length = MapX * MapZ. Lets the renderer flag underwater entities.
    public required float[] TileWaterY { get; init; }
    public required int MapX { get; init; }
    public required int MapZ { get; init; }
    public int VertexCount => Vertices.Length / 3;
    public int IndexCount => Indices.Length;
}
