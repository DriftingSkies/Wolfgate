using Content.Server._WF.MeridianTraits.Hemophage;
using Content.Shared.FixedPoint;

namespace Content.Server.Medical;

public sealed partial class HealingSystem
{
    [Dependency] private WFHemophageSystem _wfHemophage = default!;

    /// <summary>Puts a blood pack's blood into a hemophage, reserve included. False for anyone else.</summary>
    private bool WfHemophageTakesBlood(EntityUid uid, FixedPoint2 amount) => _wfHemophage.TryGiveBlood(uid, amount);

    /// <summary>Whether a hemophage still has room for blood past a full bloodstream.</summary>
    private bool WfHemophageHasRoom(EntityUid uid) => _wfHemophage.BloodRoom(uid) > FixedPoint2.Zero;
}
