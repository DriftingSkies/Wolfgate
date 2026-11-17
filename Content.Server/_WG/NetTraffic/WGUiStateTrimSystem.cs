using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Atmos.Piping.Unary.EntitySystems;
using Content.Server.Holopad;
using Content.Shared.Ame.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Holopad;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;

namespace Content.Server._WG.NetTraffic;

/// <summary>
/// Keeps UI states off entities nobody has open. The engine sends every stored state of an entity to each player who
/// can see it, and sends them all again whenever one changes, so the stock systems here only set a state while their
/// window is open and this system sets it at the moment of opening.
/// </summary>
public sealed partial class WGUiStateTrimSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private GasCanisterSystem _canister = default!;
    [Dependency] private HolopadSystem _holopad = default!;
    [Dependency] private AmeControllerSystem _ame = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadarConsoleComponent, BoundUIClosedEvent>(OnRadarClosed);
        SubscribeLocalEvent<GasCanisterComponent, BoundUIOpenedEvent>(OnCanisterOpened);
        SubscribeLocalEvent<HolopadComponent, BoundUIOpenedEvent>(OnHolopadOpened);
        SubscribeLocalEvent<AmeControllerComponent, BoundUIOpenedEvent>(OnAmeOpened);
        // Shuttle consoles get theirs from ShipAlertSystem, which holds the one helm-open subscription.
    }

    /// <summary>Drops the radar's nav state, which lists every dock, once the last viewer closes it.</summary>
    private void OnRadarClosed(EntityUid uid, RadarConsoleComponent component, BoundUIClosedEvent args)
    {
        if (args.UiKey is RadarConsoleUiKey.Key && !_ui.IsUiOpen(uid, RadarConsoleUiKey.Key))
            _ui.SetUiState(uid, RadarConsoleUiKey.Key, null);
    }

    private void OnCanisterOpened(EntityUid uid, GasCanisterComponent component, BoundUIOpenedEvent args)
    {
        if (args.UiKey is GasCanisterUiKey.Key)
            _canister.WgRefreshUi((uid, component));
    }

    private void OnHolopadOpened(EntityUid uid, HolopadComponent component, BoundUIOpenedEvent args)
    {
        if (args.UiKey is HolopadUiKey.InteractionWindow or HolopadUiKey.AiActionWindow)
            _holopad.UpdateUIState((uid, component));
    }

    private void OnAmeOpened(EntityUid uid, AmeControllerComponent component, BoundUIOpenedEvent args)
    {
        if (args.UiKey is AmeControllerUiKey.Key)
            _ame.UpdateUi(uid, component);
    }
}
