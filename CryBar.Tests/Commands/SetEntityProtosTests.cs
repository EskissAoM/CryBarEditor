using System.Collections.Generic;
using System.Numerics;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class SetEntityProtosTests
{
    static ScenarioEntity Make(uint id, int proto, string name) => new()
    {
        EntityId = id, ProtoIndex = proto, ProtoName = name, PlayerId = 0,
        Position = Vector3.Zero, Rotation = Matrix3x3.Identity,
        H1Prefix = [], H1EnTail = [], H1Suffix = []
    };

    [Fact]
    public void Apply_UpdatesProtoIndexAndName()
    {
        var entities = new List<ScenarioEntity> { Make(1, 0, "villager"), Make(2, 0, "villager") };
        var t = TestFixtures.MakeMinimalTerrain();

        var cmd = SetEntityProtos.Create(entities, new uint[] { 1, 2 }, newProtoIndex: 7, newProtoName: "ox_cart");
        cmd!.Apply(t, entities);

        Assert.Equal(7, entities[0].ProtoIndex); Assert.Equal("ox_cart", entities[0].ProtoName);
        Assert.Equal(7, entities[1].ProtoIndex); Assert.Equal("ox_cart", entities[1].ProtoName);
    }

    [Fact]
    public void Undo_RestoresPerEntityOldValues()
    {
        var entities = new List<ScenarioEntity> { Make(1, 5, "scout"), Make(2, 7, "ox_cart") };
        var cmd = SetEntityProtos.Create(entities, new uint[] { 1, 2 }, newProtoIndex: 9, newProtoName: "hero");
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        cmd.Undo(TestFixtures.MakeMinimalTerrain(), entities);

        Assert.Equal(5, entities[0].ProtoIndex); Assert.Equal("scout", entities[0].ProtoName);
        Assert.Equal(7, entities[1].ProtoIndex); Assert.Equal("ox_cart", entities[1].ProtoName);
    }

    [Fact]
    public void Create_AllAtNewValue_ReturnsNull()
    {
        var entities = new List<ScenarioEntity> { Make(1, 7, "ox_cart"), Make(2, 7, "ox_cart") };
        Assert.Null(SetEntityProtos.Create(entities, new uint[] { 1, 2 }, 7, "ox_cart"));
    }

    [Fact]
    public void Hint_IsEntityField()
    {
        var entities = new List<ScenarioEntity> { Make(1, 0, "x") };
        var cmd = SetEntityProtos.Create(entities, new uint[] { 1 }, 1, "y");
        Assert.Equal(RenderHint.EntityField, cmd!.Hint);
    }
}
