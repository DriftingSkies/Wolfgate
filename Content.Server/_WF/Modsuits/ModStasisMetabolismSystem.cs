using Content.Server.Body.Systems;
using Content.Shared._WF.Modsuits.Modules;

namespace Content.Server._WF.Modsuits;

/// <summary>
/// Slows a body's metabolism while it sits in a MOD stasis bag, using the metabolizer's own multiplier event.
/// </summary>
public sealed partial class ModStasisMetabolismSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModInStasisComponent, ComponentStartup>(OnStasisStartup);
        SubscribeLocalEvent<ModInStasisComponent, ComponentShutdown>(OnStasisShutdown);
    }

    private void OnStasisStartup(Entity<ModInStasisComponent> ent, ref ComponentStartup args)
    {
        SetMultiplier(ent, true);
    }
    private void OnStasisShutdown(Entity<ModInStasisComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            SetMultiplier(ent, false);
    }

    private void SetMultiplier(Entity<ModInStasisComponent> ent, bool apply)
    {
        var ev = new ApplyMetabolicMultiplierEvent(ent.Owner, ent.Comp.Multiplier, apply);
        RaiseLocalEvent(ent.Owner, ref ev);
    }
}
