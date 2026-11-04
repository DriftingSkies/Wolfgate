using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Damage;
using Content.Shared.Weapons.Hitscan.Components;
using Robust.Shared.Player;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private DamageableSystem _wfHitscanDamage = default!;

    /// <summary>Uses ordinary shield capacity, allocation and impact effects for intercepted beams.</summary>
    public void ApplyWolfgateHitscanImpact(EntityUid shield, EntityUid hitscan, Vector2 position, float strength,
        EntityUid? gun = null, EntityUid? shooter = null)
    {
        if (strength <= 0f || !TryComp<ShipShieldComponent>(shield, out var component))
            return;
        var actualDamage = TryComp<HitscanBasicDamageComponent>(hitscan, out var damage)
            ? MathF.Max(0f, (float)(damage.Damage * _wfHitscanDamage.UniversalHitscanDamageModifier).GetTotal()) / strength
            : 0f;
        var impactStrength = actualDamage > 0f ? 1f : 0f;
        if (component.Source is { } source && TryComp<ShipShieldEmitterComponent>(source, out var emitter))
        {
            emitter.Damage += actualDamage;
            impactStrength = WFShipShieldEffects.ImpactStrength(actualDamage,
                WFShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp));
            if (TryComp<WFShipShieldVisualsComponent>(shield, out var visuals))
                UpdateWolfgateShieldHealth(shield, visuals, source);
        }
        if (impactStrength <= 0f)
            return;
        ReportWolfgateShieldAttack(component, actualDamage, null, gun, shooter);
        PlayWolfgateShieldImpact(shield, position);
        RaiseNetworkEvent(new WFShipShieldImpactEvent(GetNetEntity(shield), position, impactStrength), Filter.BroadcastMap(Transform(shield).MapID));
    }
}
