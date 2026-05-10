using System.Collections.Generic;
using System.Numerics;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class SetEntityPositionsTests
{
    static ScenarioEntity Make(uint id, Vector3 pos) => new()
    {
        EntityId = id, ProtoIndex = 0, ProtoName = "x", PlayerId = 0,
        Position = pos, Rotation = Matrix3x3.Identity,
        H1Prefix = [], H1EnTail = [], H1Suffix = []
    };

    [Fact]
    public void Apply_WritesPerEntityNewPositions()
    {
        var entities = new List<ScenarioEntity> { Make(1, Vector3.Zero), Make(2, Vector3.Zero) };
        var cmd = SetEntityPositions.Create(entities,
            new uint[] { 1, 2 },
            new[] { new Vector3(1, 0, 1), new Vector3(2, 0, 2) });
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(new Vector3(1, 0, 1), entities[0].Position);
        Assert.Equal(new Vector3(2, 0, 2), entities[1].Position);
    }

    [Fact]
    public void Undo_RestoresPerEntityOld()
    {
        var entities = new List<ScenarioEntity> { Make(1, new Vector3(5, 0, 5)) };
        var cmd = SetEntityPositions.Create(entities, new uint[] { 1 }, new[] { new Vector3(9, 0, 9) });
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        cmd.Undo (TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(new Vector3(5, 0, 5), entities[0].Position);
    }

    [Fact]
    public void Create_AllAlreadyAtNew_ReturnsNull()
    {
        var entities = new List<ScenarioEntity> { Make(1, new Vector3(5, 0, 5)) };
        Assert.Null(SetEntityPositions.Create(entities, new uint[] { 1 }, new[] { new Vector3(5, 0, 5) }));
    }

    [Fact]
    public void Hint_IsEntityField()
    {
        var entities = new List<ScenarioEntity> { Make(1, Vector3.Zero) };
        var cmd = SetEntityPositions.Create(entities, new uint[] { 1 }, new[] { new Vector3(1, 0, 0) });
        Assert.Equal(RenderHint.EntityField, cmd!.Hint);
    }
}
