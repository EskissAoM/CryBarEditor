using System.Collections.Generic;

namespace CryBarEditor.Classes;

/// <summary>
/// Bundle of GL texture handles for one TMM, plus a per-mesh-group binding to which
/// of those handles serves as basecolor / normal map for that group. Eviction owners
/// must call DisposeGl(...) to delete the texture handles before dropping the set.
/// </summary>
public sealed class PreviewTextureSet
{
    public required List<int> OwnedHandles { get; init; }
    public required Dictionary<int, (int? BaseColor, int? Normal)> MeshGroupBindings { get; init; }

    public void DisposeGl(System.Action<int> deleteHandle)
    {
        foreach (var h in OwnedHandles) deleteHandle(h);
        OwnedHandles.Clear();
        MeshGroupBindings.Clear();
    }
}
