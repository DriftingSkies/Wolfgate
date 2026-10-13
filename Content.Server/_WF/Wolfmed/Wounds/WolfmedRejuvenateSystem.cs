using System.Linq;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// What a full heal has to undo that is not a wound. <c>WoundSystem.ClearWounds</c> deletes every wound
/// entity, but sepsis lives on the body, dead tissue, tourniquets and part infections live on the parts and organ
/// damage on the organs, so without this an <c>aheal</c> left the patient septic forever off a necrotic limb that the
/// analyzer no longer showed, with a failing heart or liver.
/// </summary>
/// <remarks>
/// Answers <see cref="WolfmedRejuvenateEvent"/>, the broadcast the two Onyx rejuvenate handlers raise,
/// because both <c>RejuvenateEvent</c> pairs on a body and a part are already owned.
/// </remarks>
public sealed class WolfmedRejuvenateSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private Damage.WolfmedDegradationVisualsSystem _degradation = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedRejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(ref WolfmedRejuvenateEvent args)
    {
        var target = args.Target;
        if (TerminatingOrDeleted(target))
            return;

        // Only a body carries sepsis, but the call costs nothing on a part.
        RemComp<WolfmedSepsisComponent>(target);

        // A wound host walks its whole body; a limb healed on its own walks itself and whatever came off
        // still attached to it, which is the same set the degradation refresh below covers.
        var parts = HasComp<WoundHostComponent>(target)
            ? _body.GetBodyChildren(target)
            : HasComp<BodyPartComponent>(target)
                ? _body.GetBodyPartChildren(target)
                : [];

        foreach (var (part, _) in parts.ToArray())
        {
            if (TerminatingOrDeleted(part))
                continue;

            RemComp<WolfmedNecrosisComponent>(part);
            RemComp<WolfmedTourniquetComponent>(part);
            // INFECTION: a part's own infection is not a wound either, and a torso left infected restarts sepsis.
            RemComp<WolfmedPartInfectionComponent>(part);
            // Residue keeps burning until washed off, and a surgery left half done keeps its step markers.
            RemComp<WolfmedChemicalBurnComponent>(part);
            RemComp<WolfmedNecrosisExcisedComponent>(part);
            RemComp<WolfmedCoreHousingOpenComponent>(part);

            // Only the brain was restored before; every other organ kept its damage and its impaired band.
            foreach (var (organ, _) in _body.GetPartOrgans(part).ToArray())
            {
                if (TryComp(organ, out WolfmedOrganComponent? health) && health.Health < health.MaxHealth)
                    _organs.SetHealth((organ, health), health.MaxHealth);
            }
        }

        _degradation.Refresh(target);
    }
}
