using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Modules;

namespace Content.Server._WF.Modsuits;

/// <summary>Grants gas-ignition immunity to the wearer while the plasma stabilizer is running.</summary>
public sealed partial class ModPlasmaStabilizerSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnActivated(Entity<ModPlasmaStabilizerModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            EnsureComp<ModPlasmaStabilizerComponent>(wearer);
    }

    [SubscribeLocalEvent]
    private void OnDeactivated(Entity<ModPlasmaStabilizerModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            RemCompDeferred<ModPlasmaStabilizerComponent>(wearer);
    }

    [SubscribeLocalEvent]
    private void OnWearerChanged(Entity<ModPlasmaStabilizerModuleComponent> ent, ref ModSuitWearerChangedEvent args)
    {
        if (args.OldWearer is { } old)
            RemCompDeferred<ModPlasmaStabilizerComponent>(old);

        if (!TryComp<ModModuleComponent>(ent, out var module) || !module.Active)
            return;

        if (args.NewWearer is { } wearer)
            EnsureComp<ModPlasmaStabilizerComponent>(wearer);
    }
}
