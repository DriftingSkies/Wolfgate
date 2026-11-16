using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModChameleonModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModChameleonModuleComponent, ModModuleInstalledEvent>(OnInstalled);
        SubscribeLocalEvent<ModChameleonModuleComponent, ModModuleRemovedEvent>(OnRemoved);
    }

    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedChameleonClothingSystem _chameleon = default!;
    private void OnInstalled(Entity<ModChameleonModuleComponent> ent, ref ModModuleInstalledEvent args)
    {
        if (HasComp<ChameleonClothingComponent>(args.Suit) || MetaData(args.Suit).EntityPrototype is not { } proto)
            return;

        EntityManager.AddComponents(args.Suit, ent.Comp.Components);
        ent.Comp.Granted = true;
        _chameleon.SetSelectedPrototype(args.Suit, proto.ID, true);
    }
    private void OnRemoved(Entity<ModChameleonModuleComponent> ent, ref ModModuleRemovedEvent args)
    {
        if (!ent.Comp.Granted)
            return;

        ent.Comp.Granted = false;
        if (TerminatingOrDeleted(args.Suit))
            return;

        if (MetaData(args.Suit).EntityPrototype is { } proto)
            _chameleon.SetSelectedPrototype(args.Suit, proto.ID, true);

        _modSuit.RevokeComponents(args.Suit, ent.Comp.Components);
    }
}
