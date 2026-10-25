using Content.Shared._WF.Camera;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private WFDefaultNorthSystem _wfDefaultNorth = default!;
}
