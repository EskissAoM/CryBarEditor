using System.Numerics;

namespace CryBar.Scenario;

public sealed class EntityMarker
{
    public required Vector3 Position { get; init; }
    public required string ProtoName { get; init; }
    public required int PlayerId { get; init; }
    public required uint EntityId { get; init; }
}
