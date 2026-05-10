using System.Collections.Generic;
using System.Numerics;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class SetEntityPlayersTests
{
    static ScenarioEntity Make(uint id, byte player) => new()
    {
        EntityId = id, ProtoIndex = 0, ProtoName = "x", PlayerId = player,
        Position = Vector3.Zero, Rotation = Matrix3x3.Identity,
        H1Prefix = [], H1EnTail = [], H1Suffix = []
    };

    [Fact]
    public void Apply_WritesNewPlayer()
    {
        var entities = new List<ScenarioEntity> { Make(1, 0), Make(2, 1) };
        var cmd = SetEntityPlayers.Create(entities, new uint[] { 1, 2 }, newPlayer: 5);
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(5, entities[0].PlayerId);
        Assert.Equal(5, entities[1].PlayerId);
    }

    [Fact]
    public void Undo_RestoresPerEntityOld()
    {
        var entities = new List<ScenarioEntity> { Make(1, 2), Make(2, 7) };
        var cmd = SetEntityPlayers.Create(entities, new uint[] { 1, 2 }, newPlayer: 5);
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        cmd.Undo (TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(2, entities[0].PlayerId);
        Assert.Equal(7, entities[1].PlayerId);
    }

    [Fact]
    public void Create_AllAlreadyNew_ReturnsNull()
    {
        var entities = new List<ScenarioEntity> { Make(1, 5), Make(2, 5) };
        Assert.Null(SetEntityPlayers.Create(entities, new uint[] { 1, 2 }, 5));
    }

    [Fact]
    public void Hint_IsEntityField()
    {
        var entities = new List<ScenarioEntity> { Make(1, 0) };
        var cmd = SetEntityPlayers.Create(entities, new uint[] { 1 }, 1);
        Assert.Equal(RenderHint.EntityField, cmd!.Hint);
    }
}
