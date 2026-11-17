using Content.Server._WG.MeridianTraits.Hemophage;
using Content.Shared.FixedPoint;

namespace Content.Server.Medical;

public sealed partial class HealingSystem
{
    [Dependency] private WGHemophageSystem _wgHemophage = default!;

    /// <summary>Puts a blood pack's blood into a hemophage, reserve included. False for anyone else.</summary>
    private bool WgHemophageTakesBlood(EntityUid uid, FixedPoint2 amount) => _wgHemophage.TryGiveBlood(uid, amount);

    /// <summary>Whether a hemophage still has room for blood past a full bloodstream.</summary>
    private bool WgHemophageHasRoom(EntityUid uid) => _wgHemophage.BloodRoom(uid) > FixedPoint2.Zero;
}
