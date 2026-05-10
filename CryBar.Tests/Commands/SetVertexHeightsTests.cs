using System.Collections.Generic;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class SetVertexHeightsTests
{
    [Fact]
    public void Apply_WritesPerVertexNewValues()
    {
        var t = TestFixtures.MakeMinimalTerrain();   // 2x2 tiles, 3x3 vertices
        var cmd = SetVertexHeights.Create(t, new[] { 0, 4, 8 }, new[] { 1f, 2f, 3f });
        cmd!.Apply(t, new List<ScenarioEntity>());
        Assert.Equal(1f, t.Heights[0]);
        Assert.Equal(2f, t.Heights[4]);
        Assert.Equal(3f, t.Heights[8]);
    }

    [Fact]
    public void Undo_RestoresPerVertexOld()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        t.Heights[0] = 0.5f; t.Heights[4] = 0.7f;
        var cmd = SetVertexHeights.Create(t, new[] { 0, 4 }, new[] { 5f, 6f });
        cmd!.Apply(t, new List<ScenarioEntity>());
        cmd.Undo(t, new List<ScenarioEntity>());
        Assert.Equal(0.5f, t.Heights[0]);
        Assert.Equal(0.7f, t.Heights[4]);
    }

    [Fact]
    public void Create_AllAlreadyAtTargets_ReturnsNull()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        t.Heights[0] = 1f;
        var cmd = SetVertexHeights.Create(t, new[] { 0 }, new[] { 1f });
        Assert.Null(cmd);
    }

    [Fact]
    public void Hint_IsTerrainGeometry()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        var cmd = SetVertexHeights.Create(t, new[] { 0 }, new[] { 1f });
        Assert.Equal(RenderHint.TerrainGeometry, cmd!.Hint);
    }

    [Fact]
    public void UniqueCornerVertices_DedupesSharedCorners()
    {
        // 4x4 map (mapX = 4, rowStride = 5). Tiles 0 and 1 share corners (1,0) and (1,1).
        var verts = VertexHeightHelpers.UniqueCornerVertices(new[] { 0, 1 }, mapSizeX: 4);
        var sorted = new SortedSet<int>(verts);
        // Tile 0 corners: 0, 1, 5, 6. Tile 1: 1, 2, 6, 7. Union: {0,1,2,5,6,7}.
        Assert.Equal(new[] { 0, 1, 2, 5, 6, 7 }, sorted);
    }
}
