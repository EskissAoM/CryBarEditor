using System.Collections.Generic;
using System.Numerics;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class SetEntityRotationsTests
{
    static ScenarioEntity Make(uint id, Matrix3x3 rot) => new()
    {
        EntityId = id, ProtoIndex = 0, ProtoName = "x", PlayerId = 0,
        Position = Vector3.Zero, Rotation = rot,
        H1Prefix = [], H1EnTail = [], H1Suffix = []
    };

    [Fact]
    public void Apply_WritesPerEntityNewRotation()
    {
        var entities = new List<ScenarioEntity> { Make(1, Matrix3x3.Identity) };
        var newRot = Matrix3x3.FromYawDegrees(45);
        var cmd = SetEntityRotations.Create(entities, new uint[] { 1 }, new[] { newRot });
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(newRot, entities[0].Rotation);
    }

    [Fact]
    public void Undo_RestoresPerEntityOld()
    {
        var oldRot = Matrix3x3.FromYawDegrees(30);
        var entities = new List<ScenarioEntity> { Make(1, oldRot) };
        var cmd = SetEntityRotations.Create(entities, new uint[] { 1 }, new[] { Matrix3x3.FromYawDegrees(60) });
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        cmd.Undo (TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(oldRot, entities[0].Rotation);
    }

    [Fact]
    public void Create_AllAlreadyAtNew_ReturnsNull()
    {
        var entities = new List<ScenarioEntity> { Make(1, Matrix3x3.Identity) };
        Assert.Null(SetEntityRotations.Create(entities, new uint[] { 1 }, new[] { Matrix3x3.Identity }));
    }

    [Fact]
    public void Hint_IsEntityField()
    {
        var entities = new List<ScenarioEntity> { Make(1, Matrix3x3.Identity) };
        var cmd = SetEntityRotations.Create(entities, new uint[] { 1 }, new[] { Matrix3x3.FromYawDegrees(45) });
        Assert.Equal(RenderHint.EntityField, cmd!.Hint);
    }
}
