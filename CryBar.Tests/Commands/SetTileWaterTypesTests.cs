using System.Collections.Generic;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class SetTileWaterTypesTests
{
    [Fact]
    public void Apply_WritesNewValue()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        t.WaterType[1] = 2;
        var cmd = SetTileWaterTypes.Create(t, new[] { 1, 3 }, newValue: 5);
        cmd!.Apply(t, new List<ScenarioEntity>());
        Assert.Equal(5, t.WaterType[1]);
        Assert.Equal(5, t.WaterType[3]);
    }

    [Fact]
    public void Undo_RestoresPerTileOldValues()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        t.WaterType[1] = 2;
        t.WaterType[3] = 7;
        var cmd = SetTileWaterTypes.Create(t, new[] { 1, 3 }, newValue: 5);
        cmd!.Apply(t, new List<ScenarioEntity>());
        cmd.Undo(t, new List<ScenarioEntity>());
        Assert.Equal(2, t.WaterType[1]);
        Assert.Equal(7, t.WaterType[3]);
    }

    [Fact]
    public void Create_AllTargetsAlreadyAtNewValue_ReturnsNull()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        t.WaterType[0] = 5; t.WaterType[1] = 5;
        Assert.Null(SetTileWaterTypes.Create(t, new[] { 0, 1 }, newValue: 5));
    }

    [Fact]
    public void Hint_IsTerrainWater()
    {
        var t = TestFixtures.MakeMinimalTerrain();
        var cmd = SetTileWaterTypes.Create(t, new[] { 0 }, newValue: 1);
        Assert.Equal(RenderHint.TerrainWater, cmd!.Hint);
    }
}
