using Content.Shared._WF.SsdIndicator;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.SSDIndicator;
using Robust.Shared.Player;

namespace Content.Server._WF.SsdIndicator;

/// <summary>
/// Marks bodies that die while SSD, and clears the mark when they are revived or their player comes back.
/// </summary>
public sealed class WFDiedSsdSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SSDIndicatorComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WFDiedSsdComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnMobStateChanged(Entity<SSDIndicatorComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
        {
            RemComp<WFDiedSsdComponent>(ent);
            return;
        }

        // A player who ghosted after dying also counts as SSD, so only a death while already away marks the body.
        if (ent.Comp.IsSSD && TryComp<MindContainerComponent>(ent, out var mind) && mind.HasMind)
            EnsureComp<WFDiedSsdComponent>(ent);
    }

    private void OnPlayerAttached(Entity<WFDiedSsdComponent> ent, ref PlayerAttachedEvent args)
    {
        RemComp<WFDiedSsdComponent>(ent);
    }
}
