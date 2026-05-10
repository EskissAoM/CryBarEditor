using System;
using System.Collections.Generic;

namespace CryBar.Scenario.Editor;

/// <summary>
/// Mediator that owns the parsed scenario views (terrain + entities), the undo/redo
/// stacks, the dirty bit, and the save path. Every Phase 2 mutation flows through
/// <see cref="Execute"/>; UI subscribes to <see cref="Changed"/> for a single
/// "something happened, refresh" notification per public operation.
///
/// Dirty tracking is generation-based: each undo/redo/execute increments a
/// generation counter, and IsDirty is the comparison against the generation that
/// was current at the most recent <see cref="MarkSaved"/>. This makes "undo back
/// to the saved state" correctly clear the dirty flag.
/// </summary>
public sealed class ScenarioEditor
{
    public const int UndoStackCap = 50;

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

    /// <summary>The most recently applied (or redone) command, or null if the undo stack is empty.</summary>
    public IScenarioCommand? LastChange => _undo.Last?.Value;

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

    /// <summary>
    /// Applies a command to the parsed views, pushes it onto the undo stack,
    /// clears the redo stack, and fires <see cref="Changed"/>. A null command is
    /// a silent no-op (commands' constructors may produce null when there's
    /// nothing to do, e.g. setting a tile to its current value).
    /// </summary>
    public void Execute(IScenarioCommand? cmd)
    {
        if (cmd is null) return;

        cmd.Apply(Terrain, Entities);
        _undo.AddLast(cmd);
        _redo.Clear();
        if (_undo.Count > UndoStackCap)
        {
            // FIFO eviction: drop the OLDEST command (front of the linked list).
            // The dropped command's Apply has already happened on the live state,
            // we just lose the ability to undo past it.
            _undo.RemoveFirst();
        }
        _generation++;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;

        var cmd = _undo.Last!.Value;
        _undo.RemoveLast();
        cmd.Undo(Terrain, Entities);
        _redo.AddLast(cmd);
        // Generation is the logical position on the command timeline.
        // Undo walks one step back, so the saved generation can be revisited
        // and IsDirty correctly clears.
        _generation--;
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
        Changed?.Invoke();
    }

    /// <summary>
    /// Rewinds every command on the undo stack back to the saved state and
    /// clears both stacks. Bypasses <see cref="Undo"/> so only ONE
    /// <see cref="Changed"/> event fires for the whole rewind.
    /// </summary>
    public void Discard()
    {
        // Walk newest -> oldest, calling Undo on each. We don't push to the redo
        // stack because we clear it at the end anyway.
        while (_undo.Count > 0)
        {
            var cmd = _undo.Last!.Value;
            _undo.RemoveLast();
            cmd.Undo(Terrain, Entities);
        }
        _redo.Clear();
        // Reset generation back to the saved value so IsDirty becomes false.
        _generation = _savedGeneration;
        Changed?.Invoke();
    }

    /// <summary>
    /// Records a successful save: stamps the save path and freezes the current
    /// generation as the "clean" generation. Fires <see cref="Changed"/> so UI
    /// can refresh title bars / save buttons.
    /// </summary>
    public void MarkSaved(string path)
    {
        SavePath = path;
        _savedGeneration = _generation;
        Changed?.Invoke();
    }
}
