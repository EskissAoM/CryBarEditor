using System.Collections.Generic;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class SetTileTexturesTests
{
    static (ScenarioTerrain t, List<ScenarioEntity> e) MakeWorld()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        // 4 tiles. Pre-fill with (0, 0).
        return (t, new List<ScenarioEntity>());
    }

    [Fact]
    public void Apply_WritesNewValuesToTouchedTiles()
    {
        var (t, e) = MakeWorld();
        t.TileGroups[1] = 0; t.TileSubs[1] = 0;
        t.TileGroups[3] = 0; t.TileSubs[3] = 0;

        var cmd = SetTileTextures.Create(t, new[] { 1, 3 }, newGroup: 5, newSub: 7);
        Assert.NotNull(cmd);
        cmd!.Apply(t, e);

        Assert.Equal(5, t.TileGroups[1]); Assert.Equal(7, t.TileSubs[1]);
        Assert.Equal(5, t.TileGroups[3]); Assert.Equal(7, t.TileSubs[3]);
        Assert.Equal(0, t.TileGroups[0]); Assert.Equal(0, t.TileGroups[2]);  // untouched
    }

    [Fact]
    public void Undo_RestoresPerTileOldValues()
    {
        var (t, e) = MakeWorld();
        t.TileGroups[1] = 2; t.TileSubs[1] = 3;
        t.TileGroups[3] = 4; t.TileSubs[3] = 5;

        var cmd = SetTileTextures.Create(t, new[] { 1, 3 }, newGroup: 9, newSub: 1);
        cmd!.Apply(t, e);
        cmd.Undo(t, e);

        Assert.Equal(2, t.TileGroups[1]); Assert.Equal(3, t.TileSubs[1]);
        Assert.Equal(4, t.TileGroups[3]); Assert.Equal(5, t.TileSubs[3]);
    }

    [Fact]
    public void Create_AllTargetsAlreadyAtNewValue_ReturnsNull()
    {
        var (t, e) = MakeWorld();
        t.TileGroups[0] = 5; t.TileSubs[0] = 7;
        t.TileGroups[1] = 5; t.TileSubs[1] = 7;

        var cmd = SetTileTextures.Create(t, new[] { 0, 1 }, newGroup: 5, newSub: 7);
        Assert.Null(cmd);
    }

    [Fact]
    public void Create_SomeTargetsAlreadyAtNewValue_StillCreatesCommand()
    {
        var (t, e) = MakeWorld();
        t.TileGroups[0] = 5; t.TileSubs[0] = 7;
        t.TileGroups[1] = 0; t.TileSubs[1] = 0;

        var cmd = SetTileTextures.Create(t, new[] { 0, 1 }, newGroup: 5, newSub: 7);
        Assert.NotNull(cmd);
    }

    [Fact]
    public void Hint_IsTerrainTexture()
    {
        var (t, _) = MakeWorld();
        var cmd = SetTileTextures.Create(t, new[] { 0 }, newGroup: 1, newSub: 0);
        Assert.Equal(RenderHint.TerrainTexture, cmd!.Hint);
    }
}
