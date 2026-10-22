using Content.Shared._WF.Xenonids.MassScanner;
using Content.Shared.Actions;
using Content.Shared.Shuttles.BUIStates;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._WF.Xenonids.MassScanner;

/// <summary>Every xeno carries a radar console; this hands out the action that toggles its window.</summary>
public sealed class XenoMassScannerSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoMassScannerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<XenoMassScannerComponent, XenoMassScannerActionEvent>(OnAction);
    }

    private void OnMapInit(Entity<XenoMassScannerComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnAction(Entity<XenoMassScannerComponent> ent, ref XenoMassScannerActionEvent args)
    {
        if (args.Handled || !TryComp(ent, out ActorComponent? actor))
            return;

        args.Handled = true;
        _ui.TryToggleUi(ent.Owner, RadarConsoleUiKey.Key, actor.PlayerSession);
    }
}
