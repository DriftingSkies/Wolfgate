using Content.Shared._WF.SuitTank;
using Content.Shared.Body.Components;

namespace Content.Shared.Atmos.EntitySystems;

public abstract partial class SharedGasTankSystem
{
    [Dependency] private WFSuitTankStorageSystem _suitTank = default!;

    /// <summary>
    /// Gets the internals of whoever wears the suit holding this tank.
    /// </summary>
    private bool TryGetSuitWearerInternals(EntityUid tank, out EntityUid? internalsUid, out InternalsComponent? internalsComp)
    {
        internalsUid = null;
        internalsComp = null;

        if (!_suitTank.TryGetWearer(tank, out var wearer) || !TryComp(wearer, out internalsComp))
            return false;

        internalsUid = wearer;
        return true;
    }
}
