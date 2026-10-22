using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Fighters;
using Content.Shared.Atmos;
using Content.Shared.Destructible;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.BUIStates;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.Fighters;

/// <summary>
/// The fighter's pressurised cockpit, its cockpit screen, cursor aim from the pilot and putting the pilot out when
/// the fighter is destroyed.
/// </summary>
public sealed partial class FighterSystem : SharedFighterSystem
{
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FighterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FighterComponent, FighterCockpitActionEvent>(OnCockpitAction);
        SubscribeLocalEvent<FighterComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<FighterComponent, FighterCockpitFollowCursorMessage>(OnCockpitFollowCursor);
        SubscribeLocalEvent<FighterComponent, FighterCockpitLeaveMessage>(OnCockpitLeave);
        SubscribeLocalEvent<FighterComponent, FighterCockpitFlightModeMessage>(OnCockpitFlightMode);
        SubscribeLocalEvent<FighterComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<FighterComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<FighterPilotComponent, AtmosExposedGetAirEvent>(OnGetAir);
        SubscribeNetworkEvent<FighterCursorAimEvent>(OnCursorAim);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A drained gun comes back only once its cell is full.
        var query = EntityQueryEnumerator<FighterComponent>();
        while (query.MoveNext(out var uid, out var fighter))
        {
            if (fighter.GunRecharging && GetGunCell(uid) is { } cell && cell.Shots >= cell.Capacity)
            {
                fighter.GunRecharging = false;
                Dirty(uid, fighter);
            }
        }
    }

    private void OnMapInit(Entity<FighterComponent> ent, ref MapInitEvent args)
    {
        RefillAir(ent.Comp.Air);
    }

    protected override void PilotEntered(Entity<FighterComponent> ent, EntityUid pilot)
    {
        if (TryComp<ActorComponent>(pilot, out var actor))
            _ui.OpenUi(ent.Owner, FighterUiKey.Cockpit, actor.PlayerSession);
    }

    protected override void PilotLeft(Entity<FighterComponent> ent, EntityUid pilot)
    {
        _ui.CloseUi(ent.Owner, FighterUiKey.Cockpit, pilot);
    }

    private void OnCockpitAction(Entity<FighterComponent> ent, ref FighterCockpitActionEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.Performer, out var actor))
            return;

        args.Handled = true;
        _ui.TryToggleUi(ent.Owner, FighterUiKey.Cockpit, actor.PlayerSession);
    }

    private void OnUiOpened(Entity<FighterComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!FighterUiKey.Cockpit.Equals(args.UiKey))
            return;

        // North-up and centred on the fighter, as the pilot's own view is.
        var state = _console.GetNavState((ent.Owner, CompOrNull<RadarConsoleComponent>(ent), null),
            _console.GetAllDocks(), new EntityCoordinates(ent, Vector2.Zero), Angle.Zero);
        _ui.SetUiState(ent.Owner, FighterUiKey.Cockpit, new NavBoundUserInterfaceState(state));
    }

    private void OnCockpitFollowCursor(Entity<FighterComponent> ent, ref FighterCockpitFollowCursorMessage args)
    {
        if (args.Actor == ent.Comp.Pilot)
            ToggleFollowCursor(ent);
    }

    private void OnCockpitLeave(Entity<FighterComponent> ent, ref FighterCockpitLeaveMessage args)
    {
        if (args.Actor == ent.Comp.Pilot)
            StartExit(ent, args.Actor);
    }

    private void OnCockpitFlightMode(Entity<FighterComponent> ent, ref FighterCockpitFlightModeMessage args)
    {
        if (args.Actor == ent.Comp.Pilot && Enum.IsDefined(args.Mode))
            SetFlightMode(ent, args.Mode);
    }

    private void OnDestruction(Entity<FighterComponent> ent, ref DestructionEventArgs args)
    {
        Eject(ent);
    }

    private void OnTerminating(Entity<FighterComponent> ent, ref EntityTerminatingEvent args)
    {
        Eject(ent);
    }

    private void OnGetAir(Entity<FighterPilotComponent> ent, ref AtmosExposedGetAirEvent args)
    {
        if (args.Handled || !TryComp<FighterComponent>(ent.Comp.Fighter, out var fighter))
            return;

        RefillAir(fighter.Air);
        args.Gas = fighter.Air;
        args.Handled = true;
    }

    /// <summary>Resets the cockpit to one atmosphere of standard air at room temperature.</summary>
    private static void RefillAir(GasMixture air)
    {
        var moles = Atmospherics.OneAtmosphere * air.Volume / (Atmospherics.R * Atmospherics.T20C);
        air.Clear();
        air.Temperature = Atmospherics.T20C;
        air.AdjustMoles(Gas.Oxygen, moles * Atmospherics.OxygenStandard);
        air.AdjustMoles(Gas.Nitrogen, moles * Atmospherics.NitrogenStandard);
    }

    private void OnCursorAim(FighterCursorAimEvent msg, EntitySessionEventArgs args)
    {
        var fighterUid = GetEntity(msg.Fighter);
        if (!TryComp<FighterComponent>(fighterUid, out var fighter)
            || fighter.Pilot == null
            || fighter.Pilot != args.SenderSession.AttachedEntity)
        {
            return;
        }

        fighter.CursorAngle = msg.Angle;
    }
}
