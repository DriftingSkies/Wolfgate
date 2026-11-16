using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModHolsterModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModHolsterModuleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ModHolsterModuleComponent, ModModuleUsedEvent>(OnUsed);
    }

    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    private void OnInit(Entity<ModHolsterModuleComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Container = _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerId);
    }
    private void OnUsed(Entity<ModHolsterModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        var user = args.User;

        if (ent.Comp.Container.ContainedEntity is { } holstered)
        {
            if (!_hands.TryPickupAnyHand(user, holstered))
            {
                _popup.PopupClient(Loc.GetString("modsuit-holster-hands-full"), user, user);
                return;
            }

            _audio.PlayPredicted(ent.Comp.DrawSound, user, user);
            _popup.PopupClient(Loc.GetString("modsuit-holster-draw", ("item", holstered)), user, user);
            args.Handled = true;
            return;
        }

        if (!_hands.TryGetActiveItem(user, out var held))
        {
            _popup.PopupClient(Loc.GetString("modsuit-holster-empty"), user, user);
            return;
        }

        if (_whitelist.IsWhitelistFail(ent.Comp.Whitelist, held.Value) || !_container.Insert(held.Value, ent.Comp.Container))
        {
            _popup.PopupClient(Loc.GetString("modsuit-holster-invalid", ("item", held.Value)), user, user);
            return;
        }

        _audio.PlayPredicted(ent.Comp.HolsterSound, user, user);
        _popup.PopupClient(Loc.GetString("modsuit-holster-store", ("item", held.Value)), user, user);
        args.Handled = true;
    }
}
