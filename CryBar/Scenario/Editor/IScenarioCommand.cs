using System.Collections.Generic;

namespace CryBar.Scenario.Editor;

public interface IScenarioCommand
{
    void Apply(ScenarioTerrain terrain, List<ScenarioEntity> entities);
    void Undo (ScenarioTerrain terrain, List<ScenarioEntity> entities);
    string DisplayName { get; }
    RenderHint Hint { get; }
}
