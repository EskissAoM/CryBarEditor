using System.Collections.Generic;

namespace CryBar.Scenario;

/// Lazy snapshot of the full game-wide terrain texture list parsed from
/// terrain_types.xml.XMB. Populated once per scenario session by the editor
/// on first terrain picker open.
public sealed class TerrainTypesCache
{
    /// Flat (group, texture) tuples in display order; index maps to the picker row.
    public required IReadOnlyList<(string Group, string Texture)> All { get; init; }
}
