using Content.Shared._WG.Camera;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private WGDefaultNorthSystem _wgDefaultNorth = default!;
}
