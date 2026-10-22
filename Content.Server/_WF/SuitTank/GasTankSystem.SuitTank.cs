using Content.Shared._WF.SuitTank;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class GasTankSystem
{
    [Dependency] private WFSuitTankStorageSystem _suitTankStorage = default!;

    /// <summary>
    /// Whether the tank sits in the storage of a suit worn by its user.
    /// </summary>
    private bool InUsersSuit(EntityUid tank, EntityUid? user)
    {
        return user != null && _suitTankStorage.TryGetWearer(tank, out var wearer) && wearer == user;
    }
}
