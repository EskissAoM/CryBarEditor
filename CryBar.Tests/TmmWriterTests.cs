using CryBar.Export;
using CryBar.TMM;

namespace CryBar.Tests;

public class TmmWriterTests
{
    [Fact]
    public void Write_EmptyModel_ProducesParseableTmm()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh { Primitives = [] },
        };

        var (tmm, data, warnings) = TmmWriter.Write(model);

        Assert.NotNull(tmm);
        Assert.NotNull(data);
        var parsed = new TmmFile(tmm);
        Assert.True(parsed.ParseHeader());
        Assert.Equal(37u, parsed.Version);
    }

    [Fact]
    public void Write_SingleQuad_VertexBufferRoundTripsThroughTmmFileParse()
    {
        var model = new GlbModel
        {
            Mesh = new GlbMesh
            {
                Primitives =
                [
                    new GlbMeshPrimitive
                    {
                        MaterialName = "m",
                        Positions = [0, 0, 0,  1, 0, 0,  1, 1, 0,  0, 1, 0],
                        Normals = [0, 0, 1,   0, 0, 1,   0, 0, 1,   0, 0, 1],
                        Tangents = [1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1,  1, 0, 0, 1],
                        TexCoords = [0, 0,  1, 0,  1, 1,  0, 1],
                        Indices = [0, 1, 2,  0, 2, 3],
                    }
                ]
            },
            Materials = [new GlbMaterial { Name = "m" }],
        };

        var (tmm, data, _) = TmmWriter.Write(model);

        var parsed = new TmmFile(tmm);
        Assert.True(parsed.ParseHeader());
        Assert.True(parsed.FullyParsed);
        Assert.Equal(4u, parsed.NumVertices);
        Assert.Equal(6u, parsed.NumTriangleVerts);

        var dataFile = new TmmDataFile(data, parsed);
        Assert.True(dataFile.Parsed);
        Assert.Equal(4, dataFile.Vertices!.Length);
        Assert.Equal(6, dataFile.Indices!.Length);
    }
}
