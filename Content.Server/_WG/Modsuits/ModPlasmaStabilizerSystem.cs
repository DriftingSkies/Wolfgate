using Content.Shared._WG.Modsuits;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared._WG.Modsuits.Modules;

namespace Content.Server._WG.Modsuits;

/// <summary>Grants gas-ignition immunity to the wearer while the plasma stabilizer is running.</summary>
public sealed partial class ModPlasmaStabilizerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModPlasmaStabilizerModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModPlasmaStabilizerModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModPlasmaStabilizerModuleComponent, ModSuitWearerChangedEvent>(OnWearerChanged);
    }

    private void OnActivated(Entity<ModPlasmaStabilizerModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            EnsureComp<ModPlasmaStabilizerComponent>(wearer);
    }

    private void OnDeactivated(Entity<ModPlasmaStabilizerModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            RemCompDeferred<ModPlasmaStabilizerComponent>(wearer);
    }

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
