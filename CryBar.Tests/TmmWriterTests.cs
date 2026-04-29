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
}
