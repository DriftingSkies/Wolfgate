using System.Collections.Generic;
using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// Healing chemicals close wounds. Removed damage only takes HealingMultiplier (0.15) off a wound and stops once the
/// part's damage is gone, so a dose of bicaridine left the wound open. Every unit a metabolising reagent heals also
/// closes wolfmed.reagent_wound_mend severity off the worst open wounds of the healed types. Wounds no damage
/// removal closes (bleeds, incisions, stumps, fractures) and wounds that refuse treatment (a lodged round) are left.
/// </summary>
public sealed class WolfmedReagentWoundMendSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <summary>Closes up to <paramref name="healed"/> times the mend factor of severity across the body's wounds.</summary>
    public void Mend(EntityUid body, IReadOnlyCollection<string> types, FixedPoint2 healed,
        IReadOnlySet<TreatmentCapability> capabilities)
    {
        if (healed <= FixedPoint2.Zero || types.Count == 0 || !HasComp<WoundHostComponent>(body))
            return;

        var remaining = healed * _cfg.GetCVar(WolfmedCVars.ReagentWoundMend);
        if (remaining <= FixedPoint2.Zero)
            return;

        var candidates = new List<Entity<WoundComponent>>();
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable) ||
                !_prototypes.TryIndex(woundable.Profile, out var profile) ||
                !profile.TreatmentCapabilities.Overlaps(capabilities))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (wound.Comp.State is not (WoundState.Open or WoundState.Stabilized) ||
                    !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                    prototype.HealingMultiplier <= 0f ||
                    !prototype.DamageTypes.Keys.Any(type => types.Contains(type.Id)))
                    continue;

                candidates.Add(wound);
            }
        }

        foreach (var wound in candidates.OrderByDescending(wound => wound.Comp.Severity))
        {
            if (remaining <= FixedPoint2.Zero)
                break;

            var amount = FixedPoint2.Min(remaining, wound.Comp.Severity);
            var attempt = new WoundTreatmentAttemptEvent(wound.Comp.HoldingPart, wound.Owner, amount);
            RaiseLocalEvent(wound.Comp.HoldingPart, ref attempt);
            RaiseLocalEvent(wound.Owner, ref attempt);
            if (attempt.Cancelled)
                continue;

            if (_wounds.ChangeSeverity(wound.AsNullable(), -amount))
                remaining -= amount;
        }
    }
}
