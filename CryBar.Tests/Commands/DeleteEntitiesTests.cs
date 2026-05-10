using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CryBar.Scenario;
using CryBar.Scenario.Editor;
using CryBar.Scenario.Editor.Commands;
using Xunit;

namespace CryBar.Tests.Commands;

public class DeleteEntitiesTests
{
    static ScenarioEntity Make(uint id) => new()
    {
        EntityId = id, ProtoIndex = 0, ProtoName = $"e{id}", PlayerId = 0,
        Position = Vector3.Zero, Rotation = Matrix3x3.Identity,
        H1Prefix = [], H1EnTail = [], H1Suffix = []
    };

    [Fact]
    public void Apply_RemovesTargetedEntities_PreservesOthers()
    {
        var entities = new List<ScenarioEntity> { Make(1), Make(2), Make(3), Make(4) };
        var cmd = DeleteEntities.Create(entities, new uint[] { 2, 4 });
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(new uint[] { 1, 3 }, entities.Select(e => e.EntityId));
    }

    [Fact]
    public void Undo_RestoresEntities_AtOriginalIndices()
    {
        var entities = new List<ScenarioEntity> { Make(1), Make(2), Make(3), Make(4) };
        var cmd = DeleteEntities.Create(entities, new uint[] { 2, 4 });
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        cmd.Undo (TestFixtures.MakeMinimalTerrain(), entities);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, entities.Select(e => e.EntityId));
    }

    [Fact]
    public void Apply_Then_NewCommand_CapturesPostDeleteState()
    {
        // Sequencing test: delete {2}, then delete {3}, then undo both -> all 4 back.
        var entities = new List<ScenarioEntity> { Make(1), Make(2), Make(3), Make(4) };
        var c1 = DeleteEntities.Create(entities, new uint[] { 2 });
        c1!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        var c2 = DeleteEntities.Create(entities, new uint[] { 3 });
        c2!.Apply(TestFixtures.MakeMinimalTerrain(), entities);

        c2.Undo(TestFixtures.MakeMinimalTerrain(), entities);
        c1.Undo(TestFixtures.MakeMinimalTerrain(), entities);

        Assert.Equal(new uint[] { 1, 2, 3, 4 }, entities.Select(e => e.EntityId));
    }

    [Fact]
    public void Create_EmptyIds_ReturnsNull()
    {
        var entities = new List<ScenarioEntity> { Make(1) };
        Assert.Null(DeleteEntities.Create(entities, System.Array.Empty<uint>()));
    }

    [Fact]
    public void Hint_IsEntityList()
    {
        var entities = new List<ScenarioEntity> { Make(1) };
        var cmd = DeleteEntities.Create(entities, new uint[] { 1 });
        Assert.Equal(RenderHint.EntityList, cmd!.Hint);
    }
}
