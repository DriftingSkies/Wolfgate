using Content.Shared._WG.Newbie;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Newbie;

/// <summary>
/// Draws the learner mark over a new player's head.
/// </summary>
public sealed partial class WGNewbieIconSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGNewbieComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WGNewbieComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_mobState.IsDead(ent) || !_prototypes.TryIndex(ent.Comp.Icon, out var icon))
            return;

        args.StatusIcons.Add(icon);
    }
}
