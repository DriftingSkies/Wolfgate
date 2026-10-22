using Content.Shared._WF.SuitTank;
using Content.Shared.Atmos.Components;

namespace Content.Shared.Body.Systems;

public abstract partial class SharedInternalsSystem
{
    [Dependency] private WFSuitTankStorageSystem _suitTank = default!;

    /// <summary>
    /// The fullest connectable tank in the storage of the suit the user wears.
    /// </summary>
    private Entity<GasTankComponent>? FindSuitStorageTank(EntityUid user)
    {
        return _suitTank.FindFullestTank(user);
    }
}
