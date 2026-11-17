using Content.Shared._WG.Lattice;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Lattice;

/// <summary>Puts the Choir's mark over a member's head; the icon's own rules decide who sees it.</summary>
public sealed class WGChoirIconSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGChoirMemberComponent, GetStatusIconsEvent>(OnGetIcons);
    }

    private void OnGetIcons(Entity<WGChoirMemberComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_proto.TryIndex(ent.Comp.StatusIcon, out var icon))
            args.StatusIcons.Add(icon);
    }
}
