namespace CryBar.Scenario;

public static class ScenarioSelectionInput
{
    public static void OnLeftClick(ScenarioSelection sel, PickHit hit, bool ctrl)
    {
        if (!ctrl)
        {
            // Switch and select. Off-map is a deliberate no-op so a stray click
            // never destroys an in-progress multi-select.
            if (hit.EntityId is uint eid) sel.SelectEntity(eid);
            else if (hit.TileIdx is int tidx) sel.SelectTile(tidx);
            return;
        }

        switch (sel.Kind)
        {
            case ScenarioSelectionKind.None:
                if (hit.EntityId is uint eidNew) sel.ToggleEntity(eidNew, additive: true);
                else if (hit.TileIdx is int tidxNew) sel.ToggleTile(tidxNew, additive: true);
                break;

            case ScenarioSelectionKind.Entities:
                // Locked: only entity hits register; tile-without-entity is ignored.
                if (hit.EntityId is uint eidLock) sel.ToggleEntity(eidLock, additive: true);
                break;

            case ScenarioSelectionKind.Tiles:
                // Locked: entity hits fall through to the tile underneath. An entity
                // always sits over a tile, so TileIdx is populated for both kinds.
                if (hit.TileIdx is int tidxLock) sel.ToggleTile(tidxLock, additive: true);
                break;
        }
    }

    public static void OnRightClick(ScenarioSelection sel, PickHit hit, bool ctrl)
    {
        if (!ctrl)
        {
            sel.Clear();
            return;
        }

        switch (sel.Kind)
        {
            case ScenarioSelectionKind.Entities:
                if (hit.EntityId is uint eid) sel.RemoveEntity(eid);
                break;

            case ScenarioSelectionKind.Tiles:
                if (hit.TileIdx is int tidx) sel.RemoveTile(tidx);
                break;

            // None: no-op.
        }
    }
}
