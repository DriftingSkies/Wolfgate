using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared.FixedPoint;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class PainSystem
{
    /// <summary>Pain after suppression, counted even while the body is numb.</summary>
    public FixedPoint2 GetPainThroughNumbness(Entity<PainComponent?> entity)
    {
        return Resolve(entity, ref entity.Comp, false)
            ? GetPainBeforeAdrenaline((entity.Owner, entity.Comp), true)
            : FixedPoint2.Zero;
    }

    /// <summary>Tells a Numb trait body that its pain moved while what it feels stayed the same.</summary>
    private void WgRaiseNumbPainChanged(EntityUid uid)
    {
        if (!_net.IsServer || !HasComp<WolfmedNumbTraitComponent>(uid))
            return;

        var ev = new WolfmedNumbPainChangedEvent();
        RaiseLocalEvent(uid, ref ev);
    }
}
