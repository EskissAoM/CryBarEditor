using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioSelectionTests
{
    [Fact]
    public void New_IsEmpty_KindNone()
    {
        var s = new ScenarioSelection();
        Assert.Empty(s.Tiles);
        Assert.Empty(s.Entities);
        Assert.Equal(ScenarioSelectionKind.None, s.Kind);
    }

    [Fact]
    public void SelectTile_Replaces()
    {
        var s = new ScenarioSelection();
        s.SelectTile(3);
        s.SelectTile(5);
        Assert.Equal(new[] { 5 }, s.Tiles);
        Assert.Equal(ScenarioSelectionKind.Tiles, s.Kind);
    }

    [Fact]
    public void SelectEntity_ClearsTiles_AndFiresOnce()
    {
        var s = new ScenarioSelection();
        s.SelectTile(3);
        int fires = 0;
        s.Changed += () => fires++;
        s.SelectEntity(7u);
        Assert.Empty(s.Tiles);
        Assert.Equal(new[] { 7u }, s.Entities);
        Assert.Equal(ScenarioSelectionKind.Entities, s.Kind);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void SelectTile_ClearsEntities_AndFiresOnce()
    {
        var s = new ScenarioSelection();
        s.SelectEntity(7u);
        int fires = 0;
        s.Changed += () => fires++;
        s.SelectTile(3);
        Assert.Empty(s.Entities);
        Assert.Equal(new[] { 3 }, s.Tiles);
        Assert.Equal(ScenarioSelectionKind.Tiles, s.Kind);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void ToggleTile_Additive_AddsThenRemoves()
    {
        var s = new ScenarioSelection();
        s.ToggleTile(5, additive: true);
        Assert.Equal(new[] { 5 }, s.Tiles);
        s.ToggleTile(5, additive: true);
        Assert.Empty(s.Tiles);
        Assert.Equal(ScenarioSelectionKind.None, s.Kind);
    }

    [Fact]
    public void ToggleTile_Additive_ClearsEntitiesFirst()
    {
        var s = new ScenarioSelection();
        s.SelectEntity(7u);
        int fires = 0;
        s.Changed += () => fires++;
        s.ToggleTile(5, additive: true);
        Assert.Empty(s.Entities);
        Assert.Equal(new[] { 5 }, s.Tiles);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void ToggleTile_NonAdditive_BehavesLikeSelectTile()
    {
        var s = new ScenarioSelection();
        s.ToggleTile(5, additive: false);
        Assert.Equal(new[] { 5 }, s.Tiles);
        int fires = 0;
        s.Changed += () => fires++;
        s.ToggleTile(5, additive: false);
        Assert.Equal(new[] { 5 }, s.Tiles);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void RemoveTile_NoOpIfAbsent()
    {
        var s = new ScenarioSelection();
        int fires = 0;
        s.Changed += () => fires++;
        s.RemoveTile(5);
        Assert.Empty(s.Tiles);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void RemoveTile_RemovesAndFires()
    {
        var s = new ScenarioSelection();
        s.SelectTile(5);
        int fires = 0;
        s.Changed += () => fires++;
        s.RemoveTile(5);
        Assert.Empty(s.Tiles);
        Assert.Equal(ScenarioSelectionKind.None, s.Kind);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void RemoveTile_NoOpWhenKindIsEntities()
    {
        var s = new ScenarioSelection();
        s.SelectEntity(7u);
        int fires = 0;
        s.Changed += () => fires++;
        s.RemoveTile(5);
        Assert.Equal(new[] { 7u }, s.Entities);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void Clear_EmptiesAndFiresOnce()
    {
        var s = new ScenarioSelection();
        s.SelectTile(5);
        int fires = 0;
        s.Changed += () => fires++;
        s.Clear();
        Assert.Empty(s.Tiles);
        Assert.Empty(s.Entities);
        Assert.Equal(ScenarioSelectionKind.None, s.Kind);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Clear_NoOpWhenAlreadyEmpty()
    {
        var s = new ScenarioSelection();
        int fires = 0;
        s.Changed += () => fires++;
        s.Clear();
        Assert.Equal(0, fires);
    }

    [Fact]
    public void RemoveEntity_NoOpWhenKindIsTiles()
    {
        var s = new ScenarioSelection();
        s.SelectTile(5);
        int fires = 0;
        s.Changed += () => fires++;
        s.RemoveEntity(7u);
        Assert.Equal(new[] { 5 }, s.Tiles);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void ToggleEntity_Additive_ClearsTilesFirst()
    {
        var s = new ScenarioSelection();
        s.SelectTile(5);
        s.ToggleEntity(7u, additive: true);
        Assert.Empty(s.Tiles);
        Assert.Equal(new[] { 7u }, s.Entities);
        Assert.Equal(ScenarioSelectionKind.Entities, s.Kind);
    }
}
