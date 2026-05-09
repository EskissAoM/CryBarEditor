namespace CryBar.Scenario;

public sealed class TerrainMesh
{
    public const int VertexStrideFloats = 12;
    public const int VertexStrideBytes = VertexStrideFloats * sizeof(float);

    public required int MapSizeX { get; init; }
    public required int MapSizeZ { get; init; }
    public required float[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
}
