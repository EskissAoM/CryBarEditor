using System.Collections.Generic;

namespace CryBar.Scenario;

public enum ScenarioSelectionKind { None, Tiles, Entities }

// Raycast pick result. Both populated when a click hits an entity sitting over a
// tile; the dispatcher decides which to act on based on selection kind + ctrl.
public sealed record PickHit(int? TileIdx, uint? EntityId);

public sealed class ScenarioSelection
{
    readonly HashSet<int> _tiles = new();
    readonly HashSet<uint> _entities = new();

    public IReadOnlySet<int> Tiles => _tiles;
    public IReadOnlySet<uint> Entities => _entities;

    public ScenarioSelectionKind Kind =>
        _tiles.Count > 0 ? ScenarioSelectionKind.Tiles
      : _entities.Count > 0 ? ScenarioSelectionKind.Entities
      : ScenarioSelectionKind.None;

    public event System.Action? Changed;

    public void SelectTile(int idx)
    {
        if (_tiles.Count == 1 && _tiles.Contains(idx) && _entities.Count == 0) return;
        _tiles.Clear();
        _entities.Clear();
        _tiles.Add(idx);
        Changed?.Invoke();
    }

    public void SelectEntity(uint id)
    {
        if (_entities.Count == 1 && _entities.Contains(id) && _tiles.Count == 0) return;
        _tiles.Clear();
        _entities.Clear();
        _entities.Add(id);
        Changed?.Invoke();
    }

    public void ToggleTile(int idx, bool additive)
    {
        if (!additive) { SelectTile(idx); return; }
        bool entitiesCleared = _entities.Count > 0;
        if (entitiesCleared) _entities.Clear();
        bool toggled = _tiles.Add(idx) || _tiles.Remove(idx);
        if (entitiesCleared || toggled) Changed?.Invoke();
    }

    public void ToggleEntity(uint id, bool additive)
    {
        if (!additive) { SelectEntity(id); return; }
        bool tilesCleared = _tiles.Count > 0;
        if (tilesCleared) _tiles.Clear();
        bool toggled = _entities.Add(id) || _entities.Remove(id);
        if (tilesCleared || toggled) Changed?.Invoke();
    }

    public void RemoveTile(int idx)
    {
        if (!_tiles.Remove(idx)) return;
        Changed?.Invoke();
    }

    public void RemoveEntity(uint id)
    {
        if (!_entities.Remove(id)) return;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_tiles.Count == 0 && _entities.Count == 0) return;
        _tiles.Clear();
        _entities.Clear();
        Changed?.Invoke();
    }
}
