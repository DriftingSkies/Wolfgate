using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._NF.Bank.Events;
using Content.Shared._WF.Outposts;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Power.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Outposts;

/// <summary>Logistics gizmos: the multi-cell charger, the deposit terminal and the planet broadcast console.</summary>
public sealed class WFOutpostLogisticsSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFPlanetWeatherSystem _weather = default!;
    [Dependency] private WFRaidDirectorSystem _raids = default!;

    /// <summary>The least time between two broadcasts on one world, from any console.</summary>
    public static readonly TimeSpan WorldCooldown = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan ChargeInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<EntityUid, TimeSpan> _nextWorldBroadcast = new();
    private TimeSpan _nextCharge;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetBroadcastConsoleComponent, BoundUIOpenedEvent>(OnBroadcastOpened);
        SubscribeLocalEvent<WFPlanetBroadcastConsoleComponent, WFPlanetBroadcastMessage>(OnBroadcast);
        SubscribeLocalEvent<WFPlanetBroadcastConsoleComponent, WFPlanetDistressMessage>(OnDistress);
        SubscribeLocalEvent<WFDepositOnlyComponent, BoundUserInterfaceMessageAttempt>(OnDepositOnlyAttempt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _nextWorldBroadcast.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextCharge)
            return;

        _nextCharge = now + ChargeInterval;
        var consoles = EntityQueryEnumerator<WFPlanetBroadcastConsoleComponent>();
        while (consoles.MoveNext(out var consoleUid, out var console))
        {
            // Open windows learn of a raid starting or ending.
            if (_ui.IsUiOpen(consoleUid, WFPlanetBroadcastUiKey.Key))
                UpdateBroadcastUi((consoleUid, console));
        }

        var seconds = (float) ChargeInterval.TotalSeconds;
        var query = EntityQueryEnumerator<WFMultiChargerComponent, ItemSlotsComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var charger, out var slots, out var receiver))
        {
            if (!_power.IsPowered(uid, receiver))
                continue;

            foreach (var id in charger.Slots)
            {
                if (!_slots.TryGetSlot(uid, id, out var slot, slots) || slot.Item is not { } cell
                    || !TryComp<BatteryComponent>(cell, out var battery) || battery.CurrentCharge >= battery.MaxCharge)
                    continue;

                _battery.SetCharge(cell, Math.Min(battery.MaxCharge, battery.CurrentCharge + charger.ChargeRate * seconds), battery);
            }
        }
    }

    private void OnDepositOnlyAttempt(Entity<WFDepositOnlyComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (args.Message is BankWithdrawMessage)
            args.Cancel();
    }

    private void OnBroadcastOpened(Entity<WFPlanetBroadcastConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateBroadcastUi(ent);
    }

    private void UpdateBroadcastUi(Entity<WFPlanetBroadcastConsoleComponent> ent)
    {
        var name = string.Empty;
        var wait = ent.Comp.NextBroadcast - _timing.CurTime;
        if (TryGetWorld(ent, out var world))
        {
            name = CompOrNull<WFPlanetWeatherComponent>(world)?.PlanetName ?? string.Empty;
            if (_nextWorldBroadcast.TryGetValue(world, out var next) && next - _timing.CurTime > wait)
                wait = next - _timing.CurTime;
        }

        var raided = Transform(ent).GridUid is { } grid && HasComp<WFOutpostComponent>(grid) && _raids.IsRaided(grid);
        var distressWait = ent.Comp.NextDistress - _timing.CurTime;
        _ui.SetUiState(ent.Owner, WFPlanetBroadcastUiKey.Key,
            new WFPlanetBroadcastState(name, (float) Math.Max(0, wait.TotalSeconds), ent.Comp.MaxLength, raided,
                (float) Math.Max(0, distressWait.TotalSeconds)));
    }

    /// <summary>Sends a builder's message to everyone on the console's world, if neither it nor the world is cooling down.</summary>
    private void OnBroadcast(Entity<WFPlanetBroadcastConsoleComponent> ent, ref WFPlanetBroadcastMessage args)
    {
        var user = args.Actor;
        if (!_power.IsPowered(ent))
            return;

        if (Transform(ent).GridUid is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !_outposts.CanBuild(user, (grid, outpost)))
        {
            _popup.PopupEntity(Loc.GetString("wf-outpost-broadcast-denied"), ent, user);
            return;
        }

        if (!TryGetWorld(ent, out var world))
        {
            _popup.PopupEntity(Loc.GetString("wf-outpost-broadcast-no-world"), ent, user);
            return;
        }

        var now = _timing.CurTime;
        if (now < ent.Comp.NextBroadcast || _nextWorldBroadcast.TryGetValue(world, out var next) && now < next)
        {
            _popup.PopupEntity(Loc.GetString("wf-outpost-broadcast-cooldown"), ent, user);
            UpdateBroadcastUi(ent);
            return;
        }

        var text = args.Text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (text.Length == 0 || text.Length > ent.Comp.MaxLength)
            return;

        if (args.Position)
            text = Loc.GetString("wf-outpost-broadcast-with-position", ("text", text), ("position", Position(ent)));

        ent.Comp.NextBroadcast = now + ent.Comp.Cooldown;
        _nextWorldBroadcast[world] = now + WorldCooldown;
        var filter = Filter.Empty().AddWhere(session => session.AttachedEntity is { } mob
            && Transform(mob).MapUid is { } map && _weather.TryGetWorld(map, out var on) && on == world);
        _chat.DispatchFilteredAnnouncement(filter, text, ent,
            Loc.GetString("wf-outpost-broadcast-sender", ("outpost", Name(grid))), colorOverride: Color.Gold);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} broadcast to {ToPrettyString(world)} from outpost {ToPrettyString(grid):grid}: {text}");
        UpdateBroadcastUi(ent);
    }

    /// <summary>
    /// Calls everyone in the sector to an outpost under raid, once in a while, whatever the broadcast cooldowns. Anyone
    /// at the console may call, not only builders.
    /// </summary>
    private void OnDistress(Entity<WFPlanetBroadcastConsoleComponent> ent, ref WFPlanetDistressMessage args)
    {
        var user = args.Actor;
        if (!_power.IsPowered(ent))
            return;

        if (Transform(ent).GridUid is not { } grid || !HasComp<WFOutpostComponent>(grid) || !_raids.IsRaided(grid))
        {
            _popup.PopupEntity(Loc.GetString("wf-outpost-distress-no-raid"), ent, user);
            return;
        }

        var now = _timing.CurTime;
        if (now < ent.Comp.NextDistress)
        {
            _popup.PopupEntity(Loc.GetString("wf-outpost-broadcast-cooldown"), ent, user);
            UpdateBroadcastUi(ent);
            return;
        }

        ent.Comp.NextDistress = now + ent.Comp.DistressCooldown;
        var world = TryGetWorld(ent, out var on) ? CompOrNull<WFPlanetWeatherComponent>(on)?.PlanetName : null;
        var text = Loc.GetString("wf-outpost-distress-text",
            ("outpost", Name(grid)), ("world", world ?? Name(Transform(grid).MapUid ?? grid)), ("position", Position(ent)));
        _chat.DispatchFilteredAnnouncement(Filter.Empty().AddWhere(session => session.AttachedEntity != null), text, ent,
            Loc.GetString("wf-outpost-distress-sender"), colorOverride: Color.Red);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} called for help from raided outpost {ToPrettyString(grid):grid}");
        UpdateBroadcastUi(ent);
    }

    /// <summary>Where a console stands on its world, in whole metres.</summary>
    private string Position(EntityUid uid)
    {
        var position = _transform.GetMapCoordinates(uid).Position;
        return Loc.GetString("wf-outpost-broadcast-position", ("x", (int) MathF.Round(position.X)), ("y", (int) MathF.Round(position.Y)));
    }

    private bool TryGetWorld(EntityUid uid, out EntityUid world)
    {
        world = default;
        return Transform(uid).MapUid is { } map && _weather.TryGetWorld(map, out world);
    }
}
