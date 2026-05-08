namespace CryBar.Scenario;

public sealed class WaterMesh
{
    public required float[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public int VertexCount => Vertices.Length / 3;
    public int IndexCount => Indices.Length;
}
