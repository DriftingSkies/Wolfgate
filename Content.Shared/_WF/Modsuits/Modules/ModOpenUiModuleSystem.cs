using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Robust.Shared.Network;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModOpenUiModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModOpenUiModuleComponent, ModModuleUsedEvent>(OnUsed);
    }

    [Dependency] private INetManager _net = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    private void OnUsed(Entity<ModOpenUiModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        if (!TryComp<ModSuitComponent>(args.Suit, out var suit))
            return;

        var target = ent.Comp.Slot is { } slot ? _modSuit.GetPart((args.Suit, suit), slot) : args.Suit;
        if (target is not { } uid || !_ui.HasUi(uid, ent.Comp.Key))
            return;

        _ui.OpenUi(uid, ent.Comp.Key, args.User, _net.IsClient);
        args.Handled = true;
    }
}
