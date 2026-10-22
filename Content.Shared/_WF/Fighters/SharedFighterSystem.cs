using Content.Shared._WF.Tether.Harpoon;
using Content.Shared.Actions;
using Content.Shared.CombatMode;
using Content.Shared.DoAfter;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Fighters;

/// <summary>
/// Boarding and leaving a fighter, the pilot's shuttle keys, its actions and when its gun may fire.
/// </summary>
public abstract partial class SharedFighterSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] protected SharedContainerSystem Container = default!;
    [Dependency] private SharedContentEyeSystem _eye = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] protected SharedTransformSystem Transforms = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FighterComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<FighterComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<FighterComponent, FighterEnterDoAfterEvent>(OnEnterDoAfter);
        SubscribeLocalEvent<FighterComponent, FighterExitDoAfterEvent>(OnExitDoAfter);
        SubscribeLocalEvent<FighterComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<FighterComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<FighterComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<FighterComponent, GunRefreshModifiersEvent>(OnGunRefresh);
        SubscribeLocalEvent<FighterComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<FighterComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<FighterComponent, FighterExitActionEvent>(OnExitAction);
        SubscribeLocalEvent<FighterComponent, FighterFollowCursorActionEvent>(OnFollowCursorAction);
        SubscribeLocalEvent<FighterPilotComponent, MannedTurretGetGunEvent>(OnGetGun);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ShuttleStrafeUp, new FighterInputCmdHandler(this, ShuttleButtons.StrafeUp))
            .Bind(ContentKeyFunctions.ShuttleStrafeDown, new FighterInputCmdHandler(this, ShuttleButtons.StrafeDown))
            .Bind(ContentKeyFunctions.ShuttleStrafeLeft, new FighterInputCmdHandler(this, ShuttleButtons.StrafeLeft))
            .Bind(ContentKeyFunctions.ShuttleStrafeRight, new FighterInputCmdHandler(this, ShuttleButtons.StrafeRight))
            .Bind(ContentKeyFunctions.ShuttleRotateLeft, new FighterInputCmdHandler(this, ShuttleButtons.RotateLeft))
            .Bind(ContentKeyFunctions.ShuttleRotateRight, new FighterInputCmdHandler(this, ShuttleButtons.RotateRight))
            .Bind(ContentKeyFunctions.ShuttleBrake, new FighterInputCmdHandler(this, ShuttleButtons.Brake))
            .Register<SharedFighterSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<SharedFighterSystem>();
    }

    private void OnInit(Entity<FighterComponent> ent, ref ComponentInit args)
    {
        ent.Comp.PilotSlot = Container.EnsureContainer<ContainerSlot>(ent, FighterComponent.PilotSlotId);
    }

    #region Boarding

    private void OnGetVerbs(Entity<FighterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Pilot != null || !CanBoard(ent, args.User))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-fighter-verb-board"),
            Priority = 2,
            Act = () =>
            {
                _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.EntryDelay,
                    new FighterEnterDoAfterEvent(), ent, target: ent)
                {
                    BreakOnMove = true,
                    NeedHand = false,
                });
            },
        });
    }

    private bool CanBoard(Entity<FighterComponent> ent, EntityUid user)
    {
        return user != ent.Owner && _whitelist.IsWhitelistPassOrNull(ent.Comp.PilotWhitelist, user);
    }

    private void OnEnterDoAfter(Entity<FighterComponent> ent, ref FighterEnterDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Pilot != null || !CanBoard(ent, args.User))
            return;

        args.Handled = true;
        if (_net.IsServer)
            Container.Insert(args.User, ent.Comp.PilotSlot);
    }

    private void OnExitAction(Entity<FighterComponent> ent, ref FighterExitActionEvent args)
    {
        if (args.Handled || ent.Comp.Pilot is not { } pilot)
            return;

        args.Handled = true;
        StartExit(ent, pilot);
    }

    /// <summary>Starts the pilot climbing out.</summary>
    protected void StartExit(Entity<FighterComponent> ent, EntityUid pilot)
    {
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, pilot, ent.Comp.ExitDelay,
            new FighterExitDoAfterEvent(), ent, target: ent)
        {
            NeedHand = false,
        });
    }

    private void OnExitDoAfter(Entity<FighterComponent> ent, ref FighterExitDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        Eject(ent);
    }

    /// <summary>Puts the pilot, if any, out beside the fighter.</summary>
    public void Eject(Entity<FighterComponent> ent)
    {
        if (!_net.IsServer || ent.Comp.Pilot is not { } pilot)
            return;

        // Nowhere to put them if the map itself is going.
        var xform = Transform(ent);
        if (TerminatingOrDeleted(xform.ParentUid) || xform.MapUid is { } map && TerminatingOrDeleted(map))
            return;

        // Removing reparents the pilot to the fighter's grid or map at its position.
        Container.Remove(pilot, ent.Comp.PilotSlot);
    }

    private void OnInserted(Entity<FighterComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != FighterComponent.PilotSlotId)
            return;

        var pilot = args.Entity;
        ent.Comp.Pilot = pilot;
        ent.Comp.CursorAngle = null;
        Dirty(ent);

        var pilotComp = EnsureComp<FighterPilotComponent>(pilot);
        pilotComp.Fighter = ent;
        Dirty(pilot, pilotComp);

        _actions.AddAction(pilot, ref ent.Comp.ExitActionEntity, ent.Comp.ExitAction, ent);
        _actions.AddAction(pilot, ref ent.Comp.FollowCursorActionEntity, ent.Comp.FollowCursorAction, ent);
        _actions.AddAction(pilot, ref ent.Comp.CockpitActionEntity, ent.Comp.CockpitAction, ent);
        _actions.SetToggled(ent.Comp.FollowCursorActionEntity, ent.Comp.FollowCursor);

        _combat.SetInCombatMode(pilot, true);
        if (HasComp<ContentEyeComponent>(pilot))
        {
            _eye.SetMaxZoom(pilot, new(ent.Comp.Zoom, ent.Comp.Zoom));
            _eye.SetZoom(pilot, new(ent.Comp.Zoom, ent.Comp.Zoom));
        }

        PilotEntered(ent, pilot);
    }

    private void OnRemoved(Entity<FighterComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != FighterComponent.PilotSlotId)
            return;

        var pilot = args.Entity;
        ent.Comp.Pilot = null;
        Dirty(ent);

        _actions.RemoveProvidedActions(pilot, ent);
        RemComp<FighterPilotComponent>(pilot);
        if (HasComp<ContentEyeComponent>(pilot))
            _eye.ResetZoom(pilot);

        PilotLeft(ent, pilot);
    }

    protected virtual void PilotEntered(Entity<FighterComponent> ent, EntityUid pilot) { }

    protected virtual void PilotLeft(Entity<FighterComponent> ent, EntityUid pilot) { }

    #endregion

    #region Controls

    private void HandleInput(EntityUid user, ShuttleButtons button, bool down)
    {
        if (!TryComp<FighterPilotComponent>(user, out var pilot))
            return;

        if (down)
            pilot.HeldButtons |= button;
        else
            pilot.HeldButtons &= ~button;
    }

    private void OnFollowCursorAction(Entity<FighterComponent> ent, ref FighterFollowCursorActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleFollowCursor(ent);
    }

    /// <summary>Switches between turning towards the cursor and turning on the rotate keys.</summary>
    protected void ToggleFollowCursor(Entity<FighterComponent> ent)
    {
        ent.Comp.FollowCursor = !ent.Comp.FollowCursor;
        ent.Comp.CursorAngle = null;
        Dirty(ent);
        _actions.SetToggled(ent.Comp.FollowCursorActionEntity, ent.Comp.FollowCursor);

        if (ent.Comp.Pilot is { } pilot)
        {
            _popup.PopupClient(Loc.GetString(ent.Comp.FollowCursor
                ? "wf-fighter-follow-cursor-on"
                : "wf-fighter-follow-cursor-off"), pilot, pilot);
        }
    }

    #endregion

    #region Gun

    private void OnGetGun(Entity<FighterPilotComponent> ent, ref MannedTurretGetGunEvent args)
    {
        args.Gun ??= ent.Comp.Fighter;
    }

    /// <summary>Takes the per-round gunshot off the gun; at this fire rate the copies drown each other out.</summary>
    private void OnGunRefresh(Entity<FighterComponent> ent, ref GunRefreshModifiersEvent args)
    {
        args.SoundGunshot = null;
    }

    private void OnGunShot(Entity<FighterComponent> ent, ref GunShotEvent args)
    {
        if (!ent.Comp.GunRecharging && GetGunCell(ent) is { Shots: <= 0 })
        {
            ent.Comp.GunRecharging = true;
            Dirty(ent);
        }

        if (Timing.CurTime < ent.Comp.NextGunSound || !Timing.IsFirstTimePredicted)
            return;

        // Read from prototype data rather than the refresh, which only runs on the server; the pilot's own
        // client plays their copy.
        ent.Comp.NextGunSound = Timing.CurTime + ent.Comp.GunSoundInterval;
        _audio.PlayPredicted(ent.Comp.GunSound ?? CompOrNull<GunComponent>(ent)?.SoundGunshot, ent, args.User);
    }

    private void OnShotAttempted(Entity<FighterComponent> ent, ref ShotAttemptedEvent args)
    {
        if (ent.Comp.GunRecharging)
        {
            args.Cancel();
            return;
        }

        var xform = Transform(ent);
        if (!ent.Comp.CanFireOnGrid && xform.GridUid != null)
        {
            args.Cancel();
            if (Timing.CurTime >= ent.Comp.NextGridPopup)
            {
                ent.Comp.NextGridPopup = Timing.CurTime + TimeSpan.FromSeconds(2);
                _popup.PopupClient(Loc.GetString("wf-fighter-no-fire-on-grid"), ent, args.User);
            }
            return;
        }
    }

    /// <summary>The battery feeding the nose gun, whichever kind of round it fires.</summary>
    public BatteryAmmoProviderComponent? GetGunCell(EntityUid fighter)
    {
        if (TryComp<ProjectileBatteryAmmoProviderComponent>(fighter, out var projectile))
            return projectile;

        return CompOrNull<HitscanBatteryAmmoProviderComponent>(fighter);
    }

    /// <summary>Holds the shot inside the cone ahead of the nose, firing along its edge when the aim is wider.</summary>
    private void OnAttemptShoot(Entity<FighterComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled || args.ToCoordinates is not { } target)
            return;

        var from = Transforms.ToMapCoordinates(args.FromCoordinates);
        var to = Transforms.ToMapCoordinates(target);
        if (from.MapId != to.MapId)
            return;

        var aim = to.Position - from.Position;
        var range = aim.Length();
        if (range < 0.01f)
            return;

        // The sprite's nose points up at rotation zero.
        var nose = Transforms.GetWorldRotation(ent) + Angle.FromDegrees(90);
        var offset = Angle.ShortestDistance(nose, aim.ToAngle());
        var half = Angle.FromDegrees(ent.Comp.FireArc);
        if (Math.Abs(offset.Theta) <= half.Theta)
            return;

        var edge = nose + half * Math.Sign(offset.Theta);
        args.ToCoordinates = Transforms.ToCoordinates(new MapCoordinates(from.Position + edge.ToVec() * range, from.MapId));
    }

    /// <summary>Switches between cruise, drive and park.</summary>
    protected void SetFlightMode(Entity<FighterComponent> ent, FighterFlightMode mode)
    {
        if (ent.Comp.FlightMode == mode)
            return;

        ent.Comp.FlightMode = mode;
        Dirty(ent);
    }

    #endregion

    private sealed class FighterInputCmdHandler(SharedFighterSystem system, ShuttleButtons button) : InputCmdHandler
    {
        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            if (session?.AttachedEntity is { } user)
                system.HandleInput(user, button, message.State == BoundKeyState.Down);

            return false;
        }
    }
}
