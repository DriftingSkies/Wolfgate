using Content.Shared._WG.Xenonids.Mobs;
using Content.Shared.Ghost;

namespace Content.Server._WG.Xenonids.Hud;

/// <summary>Gives every ghost the xeno HUD, as RMC's ghost system does. RMC's toggle action for it is not ported.</summary>
public sealed class XenoGhostHudSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<GhostComponent, ComponentInit>(OnGhostInit);
    }

    private void OnGhostInit(Entity<GhostComponent> ent, ref ComponentInit args)
    {
        EnsureComp<CMGhostXenoHudComponent>(ent);
    }
}
