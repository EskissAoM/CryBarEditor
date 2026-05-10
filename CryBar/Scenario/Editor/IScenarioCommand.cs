using System.Collections.Generic;

namespace CryBar.Scenario.Editor;

/// <summary>
/// One reversible mutation against the parsed scenario views (terrain + entities).
/// All editor mutations go through <see cref="ScenarioEditor.Execute"/> wrapped as a command;
/// the editor stores commands on the undo/redo stacks and dispatches Apply/Undo against
/// the live views it owns.
/// </summary>
public interface IScenarioCommand
{
    void Apply(ScenarioTerrain terrain, List<ScenarioEntity> entities);
    void Undo (ScenarioTerrain terrain, List<ScenarioEntity> entities);
    string DisplayName { get; }
    RenderHint Hint { get; }
}
