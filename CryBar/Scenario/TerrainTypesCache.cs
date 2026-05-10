using System.Collections.Generic;

namespace CryBar.Scenario;

/// <summary>
/// Lazy snapshot of the full game-wide terrain texture list parsed from
/// terrain_types.xml.XMB (data/map_definitions/). Populated once per scenario
/// session by the editor on first terrain picker open.
///
/// The XML structure is:
///   &lt;terraintypes&gt;
///     &lt;type name="..."&gt;        -- group; matches scenario's TerrainGroup.Name
///       &lt;uiclass uiname="..."&gt;
///         &lt;subtype ...&gt;TEXTURE_PATH&lt;/subtype&gt;  -- text content matches a TerrainGroup.Textures entry
///       &lt;/uiclass&gt;
///     &lt;/type&gt;
///   &lt;/terraintypes&gt;
/// </summary>
public sealed class TerrainTypesCache
{
    /// <summary>
    /// Group name -> list of texture paths within that group, ordered alphabetically.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ByGroup { get; init; }

    /// <summary>
    /// Flat (group, texture) tuples in display order (group sort then texture sort).
    /// Consumed directly by the picker; index into this list maps back to a (group, texture).
    /// </summary>
    public required IReadOnlyList<(string Group, string Texture)> All { get; init; }
}
