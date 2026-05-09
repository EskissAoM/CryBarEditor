using CryBar.Scenario;

namespace CryBar.Tests;

public class ScenarioSelectionInputTests
{
    static PickHit Tile(int idx) => new(idx, null);
    static PickHit Entity(uint id, int tileIdx) => new(tileIdx, id);
    static PickHit None => new(null, null);

    // ---- Left-click without Ctrl: switch and select ----

    [Fact]
    public void LeftClick_NoCtrl_TileHit_SelectsTile()
    {
        var s = new ScenarioSelection();
        s.SelectEntity(7u);
        ScenarioSelectionInput.OnLeftClick(s, Tile(5), ctrl: false);
        Assert.Equal(new[] { 5 }, s.Tiles);
        Assert.Empty(s.Entities);
    }

    [Fact]
    public void LeftClick_NoCtrl_EntityOverTile_SelectsEntity()
    {
        var s = new ScenarioSelection();
        s.SelectTile(5);
        ScenarioSelectionInput.OnLeftClick(s, Entity(7u, 12), ctrl: false);
        Assert.Equal(new[] { 7u }, s.Entities);
        Assert.Empty(s.Tiles);
    }

    [Fact]
    public void LeftClick_NoCtrl_OffMap_NoOp()
    {
        var s = new ScenarioSelection();
        s.SelectTile(5);
        s.SelectTile(6);
        int fires = 0; s.Changed += () => fires++;
        ScenarioSelectionInput.OnLeftClick(s, None, ctrl: false);
        Assert.Equal(0, fires);
    }

    // ---- Left-click with Ctrl: kind-locked ----

    [Fact]
    public void LeftClick_Ctrl_KindNone_TileHit_StartsTileSelection()
    {
        var s = new ScenarioSelection();
        ScenarioSelectionInput.OnLeftClick(s, Tile(5), ctrl: true);
        Assert.Equal(new[] { 5 }, s.Tiles);
    }

    [Fact]
    public void LeftClick_Ctrl_KindNone_EntityHit_StartsEntitySelection()
    {
        var s = new ScenarioSelection();
        ScenarioSelectionInput.OnLeftClick(s, Entity(7u, 0), ctrl: true);
        Assert.Equal(new[] { 7u }, s.Entities);
    }

    [Fact]
    public void LeftClick_Ctrl_KindEntities_TileWithoutEntity_Ignored()
    {
        var s = new ScenarioSelection();
        s.SelectEntity(7u);
        int fires = 0; s.Changed += () => fires++;
        ScenarioSelectionInput.OnLeftClick(s, Tile(5), ctrl: true);
        Assert.Equal(new[] { 7u }, s.Entities);
        Assert.Empty(s.Tiles);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void LeftClick_Ctrl_KindEntities_EntityHit_TogglesEntity()
    {
        var s = new ScenarioSelection();
        s.ToggleEntity(7u, additive: true);
        ScenarioSelectionInput.OnLeftClick(s, Entity(8u, 0), ctrl: true);
        Assert.Contains(7u, s.Entities);
        Assert.Contains(8u, s.Entities);
        Assert.Empty(s.Tiles);
    }

    [Fact]
    public void LeftClick_Ctrl_KindEntities_EntityOverTile_PrefersEntity()
    {
        var s = new ScenarioSelection();
        s.ToggleEntity(7u, additive: true);
        ScenarioSelectionInput.OnLeftClick(s, Entity(8u, 12), ctrl: true);
        Assert.Contains(8u, s.Entities);
        Assert.Empty(s.Tiles);
    }

    [Fact]
    public void LeftClick_Ctrl_KindTiles_EntityOverTile_FallsThroughToTile()
    {
        var s = new ScenarioSelection();
        s.ToggleTile(5, additive: true);
        ScenarioSelectionInput.OnLeftClick(s, Entity(7u, 12), ctrl: true);
        Assert.Contains(5, s.Tiles);
        Assert.Contains(12, s.Tiles);
        Assert.Empty(s.Entities);
    }

    [Fact]
    public void LeftClick_Ctrl_KindTiles_TileHit_TogglesTile()
    {
        var s = new ScenarioSelection();
        s.ToggleTile(5, additive: true);
        ScenarioSelectionInput.OnLeftClick(s, Tile(7), ctrl: true);
        Assert.Contains(5, s.Tiles);
        Assert.Contains(7, s.Tiles);
    }

    // ---- Right-click without Ctrl: clear ----

    [Fact]
    public void RightClick_NoCtrl_ClearsAll()
    {
        var s = new ScenarioSelection();
        s.ToggleTile(5, additive: true);
        s.ToggleTile(7, additive: true);
        ScenarioSelectionInput.OnRightClick(s, Tile(5), ctrl: false);
        Assert.Empty(s.Tiles);
        Assert.Empty(s.Entities);
    }

    [Fact]
    public void RightClick_NoCtrl_OffMap_AlsoClears()
    {
        var s = new ScenarioSelection();
        s.SelectEntity(7u);
        ScenarioSelectionInput.OnRightClick(s, None, ctrl: false);
        Assert.Empty(s.Entities);
    }

    // ---- Right-click with Ctrl: kind-locked remove ----

    [Fact]
    public void RightClick_Ctrl_KindEntities_EntityHit_RemovesOne()
    {
        var s = new ScenarioSelection();
        s.ToggleEntity(7u, additive: true);
        s.ToggleEntity(8u, additive: true);
        ScenarioSelectionInput.OnRightClick(s, Entity(8u, 0), ctrl: true);
        Assert.Equal(new[] { 7u }, s.Entities);
    }

    [Fact]
    public void RightClick_Ctrl_KindEntities_TileWithoutEntity_NoOp()
    {
        var s = new ScenarioSelection();
        s.ToggleEntity(7u, additive: true);
        int fires = 0; s.Changed += () => fires++;
        ScenarioSelectionInput.OnRightClick(s, Tile(5), ctrl: true);
        Assert.Contains(7u, s.Entities);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void RightClick_Ctrl_KindTiles_EntityOverTile_FallsThroughRemoveTile()
    {
        var s = new ScenarioSelection();
        s.ToggleTile(5, additive: true);
        s.ToggleTile(12, additive: true);
        ScenarioSelectionInput.OnRightClick(s, Entity(7u, 12), ctrl: true);
        Assert.Equal(new[] { 5 }, s.Tiles);
    }

    [Fact]
    public void RightClick_Ctrl_KindNone_NoOp()
    {
        var s = new ScenarioSelection();
        int fires = 0; s.Changed += () => fires++;
        ScenarioSelectionInput.OnRightClick(s, Tile(5), ctrl: true);
        Assert.Equal(0, fires);
    }
}
