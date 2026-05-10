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
    public void Apply_UsesExistingProtoTableIndex()
    {
        var entities = new List<ScenarioEntity> { Make(1, 0, "villager"), Make(2, 0, "villager") };
        var protoTable = new List<string> { "villager", "scout", "ox_cart" };
        var t = TestFixtures.MakeMinimalTerrain();

        var cmd = SetEntityProtos.Create(entities, new uint[] { 1, 2 }, "ox_cart", protoTable);
        cmd!.Apply(t, entities);

        // ox_cart was already at index 2 -- no append, both entities point to it.
        Assert.Equal(3, protoTable.Count);
        Assert.Equal(2, entities[0].ProtoIndex); Assert.Equal("ox_cart", entities[0].ProtoName);
        Assert.Equal(2, entities[1].ProtoIndex); Assert.Equal("ox_cart", entities[1].ProtoName);
    }

    [Fact]
    public void Apply_AppendsNewProtoNameToTable()
    {
        var entities = new List<ScenarioEntity> { Make(1, 0, "villager") };
        var protoTable = new List<string> { "villager", "scout" };
        var t = TestFixtures.MakeMinimalTerrain();

        var cmd = SetEntityProtos.Create(entities, new uint[] { 1 }, "hero", protoTable);
        cmd!.Apply(t, entities);

        // hero wasn't there -- should be appended at index 2.
        Assert.Equal(3, protoTable.Count);
        Assert.Equal("hero", protoTable[2]);
        Assert.Equal(2, entities[0].ProtoIndex);
        Assert.Equal("hero", entities[0].ProtoName);
    }

    [Fact]
    public void Undo_RestoresPerEntityOldValuesButKeepsAppendedTableEntry()
    {
        var entities = new List<ScenarioEntity> { Make(1, 5, "scout"), Make(2, 0, "villager") };
        var protoTable = new List<string> { "villager", "scout" };

        var cmd = SetEntityProtos.Create(entities, new uint[] { 1, 2 }, "hero", protoTable);
        cmd!.Apply(TestFixtures.MakeMinimalTerrain(), entities);
        // hero appended at index 2
        Assert.Equal(3, protoTable.Count);

        cmd.Undo(TestFixtures.MakeMinimalTerrain(), entities);

        Assert.Equal(5, entities[0].ProtoIndex); Assert.Equal("scout", entities[0].ProtoName);
        Assert.Equal(0, entities[1].ProtoIndex); Assert.Equal("villager", entities[1].ProtoName);
        // Append-only: TM entry stays even after undo.
        Assert.Equal(3, protoTable.Count);
        Assert.Equal("hero", protoTable[2]);
    }

    [Fact]
    public void Create_AllAtNewValue_AndNameAlreadyInTable_ReturnsNull()
    {
        // ox_cart already at index 2; both entities already point there with that name -> no-op.
        var entities = new List<ScenarioEntity> { Make(1, 2, "ox_cart"), Make(2, 2, "ox_cart") };
        var protoTable = new List<string> { "villager", "scout", "ox_cart" };
        Assert.Null(SetEntityProtos.Create(entities, new uint[] { 1, 2 }, "ox_cart", protoTable));
        Assert.Equal(3, protoTable.Count); // table untouched
    }

    [Fact]
    public void Create_NewName_AlwaysProducesCommandEvenIfEntityIndicesMatch()
    {
        // Edge case: caller passes a name not in the table; the append itself is
        // a real change, so the command must NOT be null even if the entity's
        // ProtoName already happened to equal the new name (shouldn't happen in
        // practice but defends against future scenarios).
        var entities = new List<ScenarioEntity> { Make(1, 0, "villager") };
        var protoTable = new List<string> { "villager" };

        var cmd = SetEntityProtos.Create(entities, new uint[] { 1 }, "scout", protoTable);
        Assert.NotNull(cmd);
        Assert.Equal(2, protoTable.Count);
        Assert.Equal("scout", protoTable[1]);
    }

    [Fact]
    public void Hint_IsEntityField()
    {
        var entities = new List<ScenarioEntity> { Make(1, 0, "x") };
        var protoTable = new List<string> { "x" };
        var cmd = SetEntityProtos.Create(entities, new uint[] { 1 }, "y", protoTable);
        Assert.Equal(RenderHint.EntityField, cmd!.Hint);
    }
}
