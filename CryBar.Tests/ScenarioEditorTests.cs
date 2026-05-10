using CryBar.Scenario;
using CryBar.Scenario.Editor;

namespace CryBar.Tests;

public class ScenarioEditorTests
{
    sealed class FakeCommand : IScenarioCommand
    {
        public int ApplyCount;
        public int UndoCount;
        public string DisplayName { get; }
        public RenderHint Hint { get; }

        public FakeCommand(string name = "fake", RenderHint hint = RenderHint.None)
        {
            DisplayName = name;
            Hint = hint;
        }

        public void Apply(ScenarioTerrain terrain, List<ScenarioEntity> entities) => ApplyCount++;
        public void Undo(ScenarioTerrain terrain, List<ScenarioEntity> entities) => UndoCount++;
    }

    static ScenarioEditor MakeEditor()
    {
        var scenario = TestFixtures.MakeMinimalScenarioFile();
        var terrain = TestFixtures.MakeMinimalTerrain();
        var entities = new List<ScenarioEntity>();
        return new ScenarioEditor(scenario, terrain, entities);
    }

    [Fact]
    public void Execute_PushesOntoUndoStack_FiresChanged_FlipsIsDirty()
    {
        var editor = MakeEditor();
        int changedFires = 0;
        editor.Changed += () => changedFires++;

        Assert.False(editor.IsDirty);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, editor.UndoCount);

        var cmd = new FakeCommand("first");
        editor.Execute(cmd);

        Assert.Equal(1, cmd.ApplyCount);
        Assert.Equal(0, cmd.UndoCount);
        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.Equal(1, editor.UndoCount);
        Assert.True(editor.IsDirty);
        Assert.Same(cmd, editor.LastChange);
        Assert.Equal(1, changedFires);
    }

    [Fact]
    public void Undo_PopsToRedoStack_RunsUndo()
    {
        var editor = MakeEditor();
        var cmd = new FakeCommand();
        editor.Execute(cmd);

        int changedFires = 0;
        editor.Changed += () => changedFires++;

        editor.Undo();

        Assert.Equal(1, cmd.ApplyCount);
        Assert.Equal(1, cmd.UndoCount);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(1, changedFires);
    }

    [Fact]
    public void Redo_PopsRedoStack_RunsApplyAgain()
    {
        var editor = MakeEditor();
        var cmd = new FakeCommand();
        editor.Execute(cmd);
        editor.Undo();

        int changedFires = 0;
        editor.Changed += () => changedFires++;

        editor.Redo();

        Assert.Equal(2, cmd.ApplyCount);
        Assert.Equal(1, cmd.UndoCount);
        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.Equal(1, changedFires);
    }

    [Fact]
    public void Execute_AfterUndo_ClearsRedoStack()
    {
        var editor = MakeEditor();
        var first = new FakeCommand("first");
        editor.Execute(first);
        editor.Undo();

        Assert.True(editor.CanRedo);

        var second = new FakeCommand("second");
        editor.Execute(second);

        Assert.False(editor.CanRedo);
        Assert.True(editor.CanUndo);
        Assert.Same(second, editor.LastChange);
    }

    [Fact]
    public void StackCap_50_DropsOldest()
    {
        var editor = MakeEditor();
        var commands = new FakeCommand[ScenarioEditor.UndoStackCap + 5];
        for (int i = 0; i < commands.Length; i++)
        {
            commands[i] = new FakeCommand($"cmd{i}");
            editor.Execute(commands[i]);
        }

        Assert.Equal(ScenarioEditor.UndoStackCap, editor.UndoCount);

        // Most recent on top of undo stack -> last command
        Assert.Same(commands[commands.Length - 1], editor.LastChange);

        // Walk all the way back: should pop exactly UndoStackCap commands.
        // The earliest 5 should have been dropped (FIFO eviction).
        for (int i = 0; i < ScenarioEditor.UndoStackCap; i++)
            editor.Undo();

        Assert.False(editor.CanUndo);
        // Earliest commands were dropped, so their UndoCount stays 0
        for (int i = 0; i < 5; i++)
            Assert.Equal(0, commands[i].UndoCount);
        // Surviving commands all undone exactly once
        for (int i = 5; i < commands.Length; i++)
            Assert.Equal(1, commands[i].UndoCount);
    }

    [Fact]
    public void Discard_RewindsAllUndo_FiresChangedOnce()
    {
        var editor = MakeEditor();
        var a = new FakeCommand("a");
        var b = new FakeCommand("b");
        var c = new FakeCommand("c");
        editor.Execute(a);
        editor.Execute(b);
        editor.Execute(c);

        int changedFires = 0;
        editor.Changed += () => changedFires++;

        editor.Discard();

        Assert.Equal(1, a.UndoCount);
        Assert.Equal(1, b.UndoCount);
        Assert.Equal(1, c.UndoCount);
        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.False(editor.IsDirty);
        Assert.Equal(1, changedFires);
    }

    [Fact]
    public void IsDirty_FalseAfterMarkSaved_ThenTrueAfterNextExecute()
    {
        var editor = MakeEditor();
        editor.Execute(new FakeCommand());

        Assert.True(editor.IsDirty);

        editor.MarkSaved("C:/tmp/path.mythscn");

        Assert.False(editor.IsDirty);
        Assert.Equal("C:/tmp/path.mythscn", editor.SavePath);

        editor.Execute(new FakeCommand());

        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void IsDirty_FalseAfterUndoBackToSavedGeneration()
    {
        var editor = MakeEditor();
        editor.Execute(new FakeCommand("a"));
        editor.MarkSaved("X");
        Assert.False(editor.IsDirty);

        editor.Execute(new FakeCommand("b"));
        Assert.True(editor.IsDirty);

        editor.Undo();
        // Generation is back to the saved generation -> not dirty
        Assert.False(editor.IsDirty);

        editor.Redo();
        // Forward of saved generation -> dirty again
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void NullCommand_IsSkipped_NoStackPush_NoChanged()
    {
        var editor = MakeEditor();
        int changedFires = 0;
        editor.Changed += () => changedFires++;

        editor.Execute(null);

        Assert.False(editor.CanUndo);
        Assert.Equal(0, editor.UndoCount);
        Assert.False(editor.IsDirty);
        Assert.Equal(0, changedFires);
        Assert.Null(editor.LastChange);
    }

    [Fact]
    public void MarkSaved_FiresChanged()
    {
        var editor = MakeEditor();
        editor.Execute(new FakeCommand());

        int changedFires = 0;
        editor.Changed += () => changedFires++;

        editor.MarkSaved("path");

        Assert.Equal(1, changedFires);
        Assert.Equal("path", editor.SavePath);
    }
}
