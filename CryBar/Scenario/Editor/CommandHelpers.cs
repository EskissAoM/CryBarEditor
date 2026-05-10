using System.Collections.Generic;

namespace CryBar.Scenario.Editor;

static class CommandHelpers
{
    public static Dictionary<uint, int> BuildIdToIndex(IReadOnlyList<ScenarioEntity> entities)
    {
        var d = new Dictionary<uint, int>(entities.Count);
        for (int i = 0; i < entities.Count; i++) d[entities[i].EntityId] = i;
        return d;
    }
}
