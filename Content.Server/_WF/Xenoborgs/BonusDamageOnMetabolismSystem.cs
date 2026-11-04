using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Body.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Melee.Metabolizer;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Xenoborgs;

/// <summary>
/// wizden's bio knife: pick a metabolizer by verb, and hits on bodies with it deal the bonus damage.
/// </summary>
public sealed partial class BonusDamageOnMetabolismSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;

    private static readonly VerbCategory Metabolizers =
        new("verb-categories-select-target", "/Textures/Interface/VerbIcons/group.svg.192dpi.png");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BonusDamageOnMetabolismComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<BonusDamageOnMetabolismComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnGetVerbs(Entity<BonusDamageOnMetabolismComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var metabolizers = ProtoMan.EnumeratePrototypes<MetabolizerTypePrototype>()
            .Where(m => !ent.Comp.ExcludedMetabolizers.Contains(m.ID))
            .OrderBy(m => m.LocalizedName);

        byte priority = 0;
        foreach (var metabolizer in metabolizers)
        {
            var id = new ProtoId<MetabolizerTypePrototype>(metabolizer.ID);
            args.Verbs.Add(new Verb
            {
                Priority = priority++,
                Category = Metabolizers,
                Disabled = ent.Comp.SelectedMetabolizer == id,
                Act = () =>
                {
                    ent.Comp.SelectedMetabolizer = id;
                    Dirty(ent);
                },
                Text = metabolizer.LocalizedName,
            });
        }
    }

    private void OnMeleeHit(Entity<BonusDamageOnMetabolismComponent> ent, ref MeleeHitEvent args)
    {
        if (ent.Comp.SelectedMetabolizer is not { } selected)
            return;

        foreach (var hit in args.HitEntities)
        {
            if (TryComp<MobStateComponent>(hit, out var mobState) && !ent.Comp.ValidMobStates.Contains(mobState.CurrentState))
                continue;

            if (!HasMetabolizer(hit, selected))
                continue;

            args.BonusDamage += ent.Comp.Damage;
            return;
        }
    }

    private bool HasMetabolizer(EntityUid body, ProtoId<MetabolizerTypePrototype> metabolizer)
    {
        foreach (var organ in _body.GetBodyOrganEntityComps<MetabolizerComponent>(body))
        {
            if (organ.Comp1.MetabolizerTypes?.Contains(metabolizer) == true)
                return true;
        }

        return false;
    }
}
