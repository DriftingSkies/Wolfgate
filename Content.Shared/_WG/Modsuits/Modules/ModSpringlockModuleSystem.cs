using Content.Shared._WG.Modsuits;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared._WG.Modsuits.Systems;

namespace Content.Shared._WG.Modsuits.Modules;

/// <summary>Applies the springlock and magnetic deployment speed change on both client and server.</summary>
public sealed partial class ModSpringlockModuleSystem : EntitySystem
{
    [Dependency] private ModSuitSystem _modSuit = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSpringlockModuleComponent, ModModuleInstalledEvent>(OnInstalled);
        SubscribeLocalEvent<ModSpringlockModuleComponent, ModModuleRemovedEvent>(OnRemoved);
    }

    private void OnInstalled(Entity<ModSpringlockModuleComponent> ent, ref ModModuleInstalledEvent args)
    {
        if (!TryComp<ModSuitComponent>(args.Suit, out var suit))
            return;

        _modSuit.ScaleActivationStep((args.Suit, suit), ent.Comp.ActivationStepMultiplier);
    }

    private void OnRemoved(Entity<ModSpringlockModuleComponent> ent, ref ModModuleRemovedEvent args)
    {
        ent.Comp.SetOff = false;
        ent.Comp.SnapAt = null;

        if (!TryComp<ModSuitComponent>(args.Suit, out var suit)
            || ent.Comp.ActivationStepMultiplier <= 0f)
            return;

        _modSuit.ScaleActivationStep((args.Suit, suit), 1f / ent.Comp.ActivationStepMultiplier);
    }
}
