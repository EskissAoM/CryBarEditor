using System;
using System.Collections.Generic;

namespace CryBar.Scenario.Editor;

// Mediator owning the parsed scenario views, undo/redo stacks, dirty bit, and save path.
// Dirty tracking is generation-based so undo back to the saved state clears the flag.
public sealed class ScenarioEditor
{
    public const int UndoStackCap = 50;

    // Used when a branched-history Execute clears redo and makes the saved
    // generation unreachable; keeps IsDirty true until the next MarkSaved.
    const int SavedGenerationLost = int.MinValue;

    readonly LinkedList<IScenarioCommand> _undo = new();
    readonly LinkedList<IScenarioCommand> _redo = new();

    int _generation;
    int _savedGeneration;

    public ScenarioFile Scenario { get; }
    public ScenarioTerrain Terrain { get; }
    public List<ScenarioEntity> Entities { get; }

    public string? SavePath { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => _generation != _savedGeneration;
    public int UndoCount => _undo.Count;

    // Most recently applied/undone/redone command. Set explicitly on every
    // state change because after Undo, _undo.Last is the PREVIOUS command.
    public IScenarioCommand? LastChange { get; private set; }

    public event Action? Changed;

    public ScenarioEditor(ScenarioFile scenario, ScenarioTerrain terrain, List<ScenarioEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(entities);
        Scenario = scenario;
        Terrain = terrain;
        Entities = entities;
    }

    // Null cmd is a silent no-op (Create() returns null when nothing changed).
    public void Execute(IScenarioCommand? cmd)
    {
        if (cmd is null) return;

        cmd.Apply(Terrain, Entities);
        _undo.AddLast(cmd);
        if (_undo.Count > UndoStackCap)
            _undo.RemoveFirst(); // FIFO eviction: lose ability to undo past it

        // Branched-history guard: clearing redo here makes the saved generation
        // unreachable; flag IsDirty until the next MarkSaved.
        if (_redo.Count > 0 && _savedGeneration > _generation)
            _savedGeneration = SavedGenerationLost;
        _redo.Clear();

        _generation++;
        LastChange = cmd;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;

        var cmd = _undo.Last!.Value;
        _undo.RemoveLast();
        cmd.Undo(Terrain, Entities);
        _redo.AddLast(cmd);
        _generation--;
        LastChange = cmd;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;

        var cmd = _redo.Last!.Value;
        _redo.RemoveLast();
        cmd.Apply(Terrain, Entities);
        _undo.AddLast(cmd);
        _generation++;
        LastChange = cmd;
        Changed?.Invoke();
    }

    // Rewinds all commands and clears both stacks; fires Changed once.
    public void Discard()
    {
        while (_undo.Count > 0)
        {
            var cmd = _undo.Last!.Value;
            _undo.RemoveLast();
            cmd.Undo(Terrain, Entities);
        }
        _redo.Clear();
        _generation = _savedGeneration;
        LastChange = null;
        Changed?.Invoke();
    }

    public void MarkSaved(string path)
    {
        SavePath = path;
        _savedGeneration = _generation;
        Changed?.Invoke();
    }
}
