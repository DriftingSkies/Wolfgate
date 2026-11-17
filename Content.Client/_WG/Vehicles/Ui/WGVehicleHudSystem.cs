using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Shared._WG.Vehicles.Audio;
using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared._WG.Vehicles.Grid.Components;
using Content.Shared._WG.Vehicles.Hardpoint;
using Content.Shared._WG.Vehicles.Turret;
using Content.Shared._WG.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.Player;
using Robust.Shared.Timing;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Vehicles.Ui;

/// <summary>Seat-specific driving and gunnery instruments with server-authorized vehicle controls.</summary>
public sealed partial class WGVehicleHudSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private VehicleHardpointAmmoSystem _ammunition = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private VehicleTurretSystem _turrets = default!;

    private const float DialSize = 134;

    private readonly WGVehicleEngineComponent _unpowered = new();
    private WGVehicleDashboard _panel = default!;
    private WGGlassGauge[] _gauges = Array.Empty<WGGlassGauge>();
    private BoxContainer _driverGauges = default!;
    private BoxContainer _supplies = default!;
    private BoxContainer _gunnery = default!;
    private bool _powered;
    private float _startup;
    private EntityUid? _lastVehicle;
    private Label _role = default!;
    private Label _engineState = default!;
    private float _speedValue;
    private float _speedLimit = 60f;
    private float _rpmValue;
    private float _fuelValue;
    private float _batteryValue;
    private Label _weapon = default!;
    private Label _ammo = default!;
    private WGVehicleRoundButton _key = default!;
    private WGVehicleRoundButton _startStop = default!;
    private WGVehicleRoundButton _brake = default!;
    private WGVehicleRoundButton _headlights = default!;
    private WGVehicleRoundButton _horn = default!;
    private WGVehicleRoundButton _turretLock = default!;
    private EntityUid? _vehicle;

    public override void Initialize()
    {
        _panel = new WGVehicleDashboard();
        _role = WGInstrumentTheme.Label("wg-vehicle-hud-driver");
        _engineState = new Label { ClipText = true, HorizontalExpand = true };
        _role.MouseFilter = _engineState.MouseFilter = Control.MouseFilterMode.Ignore;
        _weapon = new Label { ClipText = true, HorizontalExpand = true };
        _ammo = new Label();

        var speed = new WGGlassGauge("wg-vehicle-gauge-speed", () => ReadGauge(_speedValue, _speedLimit, "wg-vehicle-unit-speed"));
        var rpm = new WGGlassGauge("wg-vehicle-gauge-rpm", () => ReadGauge(_rpmValue, 3000, "wg-vehicle-unit-rpm"));
        var fuel = new WGGlassGauge("wg-vehicle-gauge-fuel", () => ReadGauge(_fuelValue, 100, "wg-vehicle-unit-percent"), strip: true)
            { CompactStrip = true, SetHeight = 52, HorizontalExpand = true };
        var battery = new WGGlassGauge("wg-vehicle-gauge-battery", () => ReadGauge(_batteryValue, 100, "wg-vehicle-unit-percent"), strip: true)
            { CompactStrip = true, SetHeight = 52, HorizontalExpand = true };
        _gauges = new[] { rpm, speed, fuel, battery };
        foreach (var dial in new[] { speed, rpm })
        {
            dial.SetSize = new Vector2(DialSize, DialSize);
            dial.MinWidth = DialSize;
            dial.HorizontalExpand = false;
        }

        _driverGauges = WGInstrumentTheme.Row(speed, rpm);
        _driverGauges.HorizontalAlignment = Control.HAlignment.Center;
        _driverGauges.HorizontalExpand = false;
        _supplies = WGInstrumentTheme.Row(fuel, battery);
        _gunnery = WGInstrumentTheme.Column(_weapon, _ammo);

        _key = new WGVehicleRoundButton(WGVehicleGlyph.Key);
        _startStop = new WGVehicleRoundButton(WGVehicleGlyph.Power) { LitColor = WGInstrumentTheme.Green };
        _brake = new WGVehicleRoundButton(WGVehicleGlyph.Brake, "wg-vehicle-hud-brake") { LitColor = WGInstrumentTheme.Red };
        _headlights = new WGVehicleRoundButton(WGVehicleGlyph.Headlights, "wg-vehicle-hud-headlights")
            { LitColor = WGInstrumentTheme.Skin.Caution };
        _horn = new WGVehicleRoundButton(WGVehicleGlyph.Horn, "wg-vehicle-hud-horn");
        _turretLock = new WGVehicleRoundButton(WGVehicleGlyph.TurretLock) { LitColor = WGInstrumentTheme.Skin.Caution };

        _frameHealth = new Label { MouseFilter = Control.MouseFilterMode.Ignore };
        _panel.LeftBank.AddChild(WGInstrumentTheme.Row(_role, _engineState, _frameHealth));
        _panel.LeftBank.AddChild(_driverGauges);
        _panel.LeftBank.AddChild(_gunnery);
        _panel.RightBank.AddChild(_supplies);
        InitializeVehicleControls(_key, _startStop, _brake, _headlights, _horn, _turretLock);
        WGInstrumentTheme.Install(_panel);

        _key.OnPressed += _ => Send(new WGVehicleControlRequest(Net(), false) { Ignition = true });
        _startStop.OnPressed += _ => Send(new WGVehicleControlRequest(Net(), false) { StartStop = true });
        _brake.OnPressed += _ => Send(new WGVehicleControlRequest(Net(), true));
        _headlights.OnPressed += _ =>
        {
            if (_vehicle is { } uid)
                RaiseNetworkEvent(new VehicleSpotlightToggleRequestEvent(GetNetEntity(uid)));
        };
        _turretLock.OnPressed += _ =>
        {
            if (_vehicle is { } uid)
                RaiseNetworkEvent(new WGTurretStabilizeRequestEvent(GetNetEntity(uid), !_turretLock.Lit));
        };
        _horn.OnPressed += _ =>
        {
            if (_vehicle is { } uid)
                RaiseNetworkEvent(new VehicleHornRequestEvent(GetNetEntity(uid)));
        };
    }

    private NetEntity Net() => _vehicle is { } uid ? GetNetEntity(uid) : NetEntity.Invalid;

    private void Send(WGVehicleControlRequest request)
    {
        if (_vehicle != null)
            RaiseNetworkEvent(request);
    }

    public override void Shutdown()
    {
        _panel.Orphan();
        _panel.Dispose();
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        _vehicle = null;
        var driver = false;
        EntityUid? selected = null;
        if (_player.LocalEntity is { } user)
        {
            if (TryComp(user, out RMCVehicleOperatorComponent? op) && op.Vehicle is { } driven)
            {
                _vehicle = driven;
                driver = true;
            }
            else if (TryComp(user, out VehicleWeaponsOperatorComponent? gunner))
            {
                _vehicle = gunner.Vehicle;
                selected = gunner.SelectedWeapon;
            }
            else if (TryComp(user, out VehicleLockActionComponent? occupant))
                _vehicle = occupant.Vehicle;
            else if (HasComp<RMCVehicleComponent>(user))
                _vehicle = user;
        }

        // Operator components can still name a vehicle or gun that was just deleted.
        if (TerminatingOrDeleted(selected))
            selected = null;

        if (_vehicle is not { } uid || TerminatingOrDeleted(uid))
        {
            _vehicle = null;
            _panel.Orphan();
            _powered = false;
            _lastVehicle = null;
            return;
        }
        var hasEngine = TryComp(uid, out WGVehicleEngineComponent? engine);
        engine ??= _unpowered;
        driver &= hasEngine;
        if (_panel.Parent == null)
            _ui.PopupRoot.AddChild(_panel);
        RefreshVehicleControls(uid, frameTime);
        var powered = engine.Powered;
        if (powered && (!_powered || _lastVehicle != uid))
            _startup = 0f;
        else if (powered)
            _startup = MathF.Min(WGVehicleInstrumentStartup.Duration, _startup + frameTime);
        _powered = powered;
        _lastVehicle = uid;
        foreach (var gauge in _gauges)
            gauge.Powered = powered;

        _role.Text = Loc.GetString(driver ? "wg-vehicle-hud-driver" : "wg-vehicle-hud-gunner");
        var speed = 0f;
        if (TryComp(uid, out GridVehicleMoverComponent? mover))
        {
            var heading = mover.Heading;
            var velocity = mover.FreeSteering ? mover.TravelVelocity : heading.ToWorldVec() * mover.CurrentSpeed;
            speed = (velocity + mover.ImpactVelocity).Length() * 3.6f;
        }
        _speedValue = speed;
        _speedLimit = mover == null ? 60f : MathF.Max(1f, mover.MaxSpeed * 3.6f);
        _rpmValue = !engine.Running ? 0f : 700f + 2300f *
            (TryComp(uid, out WGVehicleEngineAudioComponent? audio) ? audio.Throttle : Math.Clamp(speed / _speedLimit, 0f, 1f));
        _fuelValue = Math.Clamp(100f * engine.Fuel / MathF.Max(1f, engine.Capacity), 0f, 100f);
        _batteryValue = Math.Clamp(100f * engine.BatteryCharge / MathF.Max(1f, engine.BatteryCapacity), 0f, 100f);
        PositionHud();

        var fuelWarning = WGVehicleLamp.FuelWarningLit(_fuelValue, powered, _timing.RealTime.TotalSeconds);
        _engineState.Text = Loc.GetString(fuelWarning ? "wg-vehicle-fuel-low"
            : engine.Running ? "wg-vehicle-hud-running"
            : engine.Powered ? "wg-vehicle-hud-accessory-state"
            : "wg-vehicle-hud-stopped");
        _engineState.FontColorOverride = fuelWarning ? WGInstrumentTheme.Red : null;

        _driverGauges.Visible = _supplies.Visible = driver;
        _gunnery.Visible = !driver;
        _key.Visible = _startStop.Visible = _brake.Visible = _headlights.Visible = _horn.Visible = driver;

        // The key goes in from anywhere on you and comes out to your hand; the button beside it runs the engine.
        var inserted = engine.IgnitionKey != null;
        _key.KeyInserted = inserted;
        _key.KeyOn = engine.Electrics;
        _key.Lit = engine.Electrics;
        _key.LitColor = WGInstrumentTheme.Skin.Caution;
        _key.ToolTip = Loc.GetString(inserted ? "wg-vehicle-key-remove" : "wg-vehicle-key-insert");
        _startStop.Disabled = !inserted;
        _startStop.Lit = engine.Running;
        _startStop.ToolTip = Loc.GetString(engine.Running ? "wg-vehicle-hud-stop" : "wg-vehicle-hud-start");

        _headlights.Disabled = !engine.Powered;
        _headlights.Lit = TryComp(uid, out VehicleSpotlightComponent? headlights) && headlights.Enabled;
        _brake.Lit = engine.ParkingBrake;

        // Whoever holds the guns can lock the selected turret's bearing or let it turn with the hull.
        _turretLock.Visible = false;
        if (_player.LocalEntity is { } holder &&
            TryComp(holder, out VehicleWeaponsOperatorComponent? weaponsOp) &&
            weaponsOp.Vehicle == uid &&
            weaponsOp.SelectedWeapon is { } aimed &&
            !TerminatingOrDeleted(aimed) &&
            _turrets.TryResolveRotationTarget(aimed, out _, out var turret) &&
            turret.RotateToCursor)
        {
            _turretLock.Visible = true;
            _turretLock.Lit = turret.StabilizedRotation;
            _turretLock.ToolTip = Loc.GetString(turret.StabilizedRotation ? "wg-vehicle-hud-turret-locked" : "wg-vehicle-hud-turret-free");
        }

        _weapon.Text = selected is { } gun ? Loc.GetString("wg-vehicle-hud-weapon", ("weapon", Name(gun))) : Loc.GetString("wg-vehicle-hud-no-weapon");
        _weapon.ToolTip = _weapon.Text;
        _ammo.Text = string.Empty;
        if (selected is { } weapon && TryComp(weapon, out VehicleHardpointAmmoComponent? ammo))
        {
            var count = new GetAmmoCountEvent();
            RaiseLocalEvent(weapon, ref count);
            _ammo.Text = Loc.GetString("wg-vehicle-hud-ammo", ("ready", count.Count),
                ("rounds", _ammunition.GetStoredRounds(ammo, ammo.MagazineSize)));
        }
    }

    /// <summary>Fits the dashboard beside the existing hand slots without intercepting their input.</summary>
    private void PositionHud()
    {
        var root = _ui.PopupRoot;
        var hotbar = _ui.GetActiveUIWidgetOrNull<HotbarGui>();
        var bounds = UIBox2.FromDimensions(new Vector2((root.Width - 760) / 2, root.Height - 80), new Vector2(760, 80));
        if (hotbar is { Visible: true, Width: > 0, Height: > 0 })
            bounds = UIBox2.FromDimensions(hotbar.GlobalPosition - root.GlobalPosition, hotbar.Size);
        LayoutContainer.SetPosition(_panel, Vector2.Zero);
        _panel.Fit(root.Size, bounds);
    }

    private WGGaugeReading ReadGauge(float actual, float maximum, string unit)
    {
        return WGGaugeReading.Number(_powered ? WGVehicleInstrumentStartup.Reading(actual, maximum, _startup) : 0,
            0, maximum, unit);
    }
}
