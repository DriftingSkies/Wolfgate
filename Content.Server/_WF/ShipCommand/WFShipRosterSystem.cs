using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.Traders;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Traders;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Mobs;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// Staffs a commanded ship's posts: crew posts with NpcCrew crewmen of one group and mission, counter posts with
/// station traders. Frees a post when its occupant dies or is removed, and restaffs it by hand or, when switched on,
/// by itself after a delay within an hourly cap. A post can be stood down, or held by a player who took its job; its
/// crewman is then relieved and walks to the crew bay to leave. Crew spawn one at a time through
/// <see cref="WFCrewSystem.SpawnCrewman"/> and <see cref="WFCrewSetupSystem.ApplyMission"/>, which leave the rest of the
/// crew and the grid's faction alone; restaffed crew come aboard at the crew bay and walk to their posts.
/// </summary>
public sealed class WFShipRosterSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TraderSystem _traders = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewSetupSystem _setup = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFShipBehaviourSystem _behaviour = default!;

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    /// <summary>How long a relieved crewman with no crew bay to walk to lingers after his last word.</summary>
    private static readonly TimeSpan ReliefLinger = TimeSpan.FromSeconds(3);

    private const int ReliefLines = 3;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipRosterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFShipRosterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFShipRosterMemberComponent, MobStateChangedEvent>(OnMemberState);
        SubscribeLocalEvent<WFShipRosterMemberComponent, EntityTerminatingEvent>(OnMemberTerminating);
    }

    private void OnMapInit(Entity<WFShipRosterComponent> grid, ref MapInitEvent args)
    {
        Begin(grid);
    }

    /// <summary>A roster added to a grid already running (an admin vessel's added components) starts at once.</summary>
    private void OnStartup(Entity<WFShipRosterComponent> grid, ref ComponentStartup args)
    {
        if (MetaData(grid).EntityLifeStage >= EntityLifeStage.MapInitialized)
            Begin(grid);
    }

    /// <summary>Reads the posts and, for a spawning ship, staffs them where they stand. Safe to repeat.</summary>
    private void Begin(Entity<WFShipRosterComponent> grid)
    {
        ReadPosts(grid);
        if (grid.Comp.StaffOnSpawn)
            Fill(grid, null, null, atPost: true);
        _behaviour.ApplyAll(grid);
    }

    /// <summary>Adds a slot for every crew and counter post marker on the grid that has none yet, and finds the crew bay.</summary>
    public void ReadPosts(Entity<WFShipRosterComponent> grid)
    {
        var counts = new Dictionary<string, int>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var xform = Transform(child);
            if (HasComp<WFShipCrewBayComponent>(child))
            {
                grid.Comp.Bay ??= xform.Coordinates;
                continue;
            }

            WFShipRosterSlot slot;
            string id;
            if (TryComp<WFShipCrewPostComponent>(child, out var post))
            {
                id = SlotId(post.Slot, post.Role.Id, counts);
                slot = new WFShipRosterSlot { Kind = WFShipRosterKind.Crew, Role = post.Role, Loadout = post.Loadout };
            }
            else if (TryComp<WFShipCounterPostComponent>(child, out var counter))
            {
                id = SlotId(counter.Slot, counter.Trader.Id, counts);
                slot = new WFShipRosterSlot { Kind = WFShipRosterKind.Counter, Trader = counter.Trader };
            }
            else
            {
                continue;
            }

            if (grid.Comp.Slots.ContainsKey(id))
                continue;

            slot.Id = id;
            slot.Post = xform.Coordinates;
            slot.Rotation = xform.LocalRotation;
            grid.Comp.Slots[id] = slot;
        }
    }

    private static string SlotId(string given, string what, Dictionary<string, int> counts)
    {
        if (!string.IsNullOrEmpty(given))
            return given;

        var n = counts.GetValueOrDefault(what) + 1;
        counts[what] = n;
        return $"{what}-{n}";
    }

    /// <summary>Whether a slot's post is held by a living crewman or a trader.</summary>
    public bool Held(WFShipRosterSlot slot)
    {
        return slot.Occupant is { } mob && !TerminatingOrDeleted(mob) && slot.Down == null;
    }

    /// <summary>Whether nobody should be sent to a slot: someone holds it, a player has its job, it is stood down, or the crewman stood down from it hasn't left yet.</summary>
    public bool Closed(WFShipRosterSlot slot)
    {
        return Held(slot) || slot.Holder != null || slot.StoodDown || slot.Leaving is { } leaving && !TerminatingOrDeleted(leaving);
    }

    /// <summary>
    /// Staffs one slot, or every slot when <paramref name="slotId"/> is null, wherever the post is open. Crew come aboard
    /// at the crew bay and walk to their posts. Returns how many were sent. Admin-logged with the actor.
    /// </summary>
    public int Staff(Entity<WFShipRosterComponent> grid, string? slotId, EntityUid? actor)
    {
        return Fill(grid, slotId, actor, atPost: false);
    }

    private int Fill(Entity<WFShipRosterComponent> grid, string? slotId, EntityUid? actor, bool atPost)
    {
        var staffed = 0;
        foreach (var slot in grid.Comp.Slots.Values.ToList())
        {
            if (slotId != null && slot.Id != slotId || Closed(slot))
                continue;

            if (SpawnInto(grid, slot, atPost))
                staffed++;
        }

        if (staffed > 0)
        {
            if (actor is { } user)
                _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):actor} staffed {staffed} posts on {ToPrettyString(grid.Owner):grid}");
            else
                _adminLog.Add(LogType.Action, LogImpact.Low, $"{staffed} posts staffed on {ToPrettyString(grid.Owner):grid}");
        }

        return staffed;
    }

    /// <summary>Switches automatic restaffing. False when nothing changed.</summary>
    public bool SetAutoRestaff(Entity<WFShipRosterComponent> grid, bool on, EntityUid? actor)
    {
        if (grid.Comp.AutoRestaff == on)
            return false;

        grid.Comp.AutoRestaff = on;
        if (actor is { } user)
            _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):actor} turned automatic restaffing {(on ? "on" : "off")} on {ToPrettyString(grid.Owner):grid}");
        return true;
    }

    /// <summary>
    /// Stands a post down, relieving whoever holds it, or stands it up again, when automatic restaffing may fill it.
    /// False when there is no such post or nothing changed.
    /// </summary>
    public bool SetStoodDown(Entity<WFShipRosterComponent> grid, string slotId, bool down, EntityUid? actor)
    {
        if (!grid.Comp.Slots.TryGetValue(slotId, out var slot) || slot.StoodDown == down)
            return false;

        slot.StoodDown = down;
        if (down)
            Relieve(grid, slot, null);
        else
            slot.Down ??= _timing.CurTime;

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):actor} stood post {slot.Id} on {ToPrettyString(grid.Owner):grid} {(down ? "down" : "up")}");
        return true;
    }

    /// <summary>
    /// Gives a post to a player who took its job: its crewman or trader is relieved, and nobody is sent to it while
    /// the player holds it.
    /// </summary>
    public void Hold(Entity<WFShipRosterComponent> grid, WFShipRosterSlot slot, EntityUid holder)
    {
        slot.Holder = holder;
        Relieve(grid, slot, holder);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(holder):player} took post {slot.Id} on {ToPrettyString(grid.Owner):grid}");
    }

    /// <summary>Hands a player's post back to the roster, which restaffs it by its usual rule.</summary>
    public void Release(Entity<WFShipRosterComponent> grid, WFShipRosterSlot slot)
    {
        if (slot.Holder is not { } holder)
            return;

        slot.Holder = null;
        if (!Held(slot))
            slot.Down = _timing.CurTime;

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(holder):player} left post {slot.Id} on {ToPrettyString(grid.Owner):grid}");
    }

    /// <summary>
    /// Takes a post's occupant off it. A trader closes his counter and goes at once; a crewman says a word to whoever
    /// relieves him and walks to the crew bay, where he leaves the ship.
    /// </summary>
    private void Relieve(Entity<WFShipRosterComponent> grid, WFShipRosterSlot slot, EntityUid? by)
    {
        var occupant = slot.Occupant;
        slot.Occupant = null;
        slot.Down = null;
        if (occupant is not { } mob || TerminatingOrDeleted(mob))
            return;

        RemComp<WFShipRosterMemberComponent>(mob);
        if (TryComp<TraderComponent>(mob, out var trader))
        {
            _traders.EndConversation((mob, trader));
            QueueDel(mob);
            return;
        }

        if (by == null)
            slot.Leaving = mob;

        var word = new WFShipPostRelievedEvent(grid, by);
        RaiseLocalEvent(mob, ref word);
        if (!word.Spoke)
        {
            var line = by is { } player
                ? Loc.GetString($"wf-shipcommand-relieved-{_random.Next(1, ReliefLines + 1)}", ("name", Name(player)))
                : Loc.GetString("wf-shipcommand-stood-down");
            _chat.TrySendInGameICMessage(mob, line, InGameICChatType.Speak, ChatTransmitRange.Normal);
        }

        // Off duty: no helm, no guns, no fighting, no boarders to answer; the crew bay is his post now.
        RemComp<WFPilotDutyComponent>(mob);
        RemComp<WFGunnerDutyComponent>(mob);
        RemComp<WFCrewSecurityComponent>(mob);
        RemComp<WFCrewMedicComponent>(mob);
        if (TryComp<WFCrewComponent>(mob, out var crew))
        {
            crew.Duty = WFCrewDuties.Guard;
            crew.Engagement = WFCrewEngagement.Never;
            crew.Post = grid.Comp.Bay;
            _crew.Apply((mob, crew));
        }

        var relieved = EnsureComp<WFShipRelievedComponent>(mob);
        relieved.Bay = grid.Comp.Bay;
        relieved.Deadline = _timing.CurTime + (grid.Comp.Bay == null ? ReliefLinger : grid.Comp.ReliefTimeout);
    }

    private bool SpawnInto(Entity<WFShipRosterComponent> grid, WFShipRosterSlot slot, bool atPost)
    {
        if (TerminatingOrDeleted(slot.Post.EntityId))
            return false;

        EntityUid mob;
        var fromBay = false;
        if (slot.Kind == WFShipRosterKind.Counter)
        {
            if (slot.Trader is not { } proto)
                return false;

            mob = Spawn(proto, slot.Post);
            _transform.SetLocalRotation(mob, slot.Rotation);
            // Map init looked for the counter before the trader was turned to face it.
            if (TryComp<TraderComponent>(mob, out var trader))
                _traders.RefreshTable((mob, trader));
        }
        else
        {
            var arrival = atPost || grid.Comp.Bay is not { } bay ? slot.Post : bay;
            if (_crew.SpawnCrewman(slot.Role, arrival, grid.Comp.Group, slot.Loadout) is not { } crewman)
                return false;

            mob = crewman;
            _setup.ApplyMission(mob, grid, Mission(grid.Comp));
            if (grid.Comp.InfiniteAmmo && TryComp<WFCrewComponent>(mob, out var crewComp))
                crewComp.InfiniteAmmo = true;
            fromBay = arrival != slot.Post;
            if (fromBay)
                _crew.SetPost(mob, slot.Post);
        }

        // The fallen keep their bodies; only the newcomer answers for the post.
        if (slot.Occupant is { } old && !TerminatingOrDeleted(old))
            RemComp<WFShipRosterMemberComponent>(old);

        var member = EnsureComp<WFShipRosterMemberComponent>(mob);
        member.Grid = grid;
        member.Slot = slot.Id;
        slot.Occupant = mob;
        slot.Down = null;
        _behaviour.ApplyTo(grid, mob);
        var staffed = new WFShipPostStaffedEvent(mob, grid, slot.Id, fromBay);
        RaiseLocalEvent(mob, ref staffed, true);
        // The newcomer's card opens the zone doors now, not at the next two-second sweep.
        EntityManager.System<WFShipCommandSystem>().RefreshZoneKeys(grid);
        return true;
    }

    private static WFCrewMission Mission(WFShipRosterComponent roster)
    {
        return new WFCrewMission
        {
            Group = roster.Group,
            Callsign = roster.Callsign,
            BoardingResponse = roster.BoardingResponse,
            DockingResponse = roster.DockingResponse,
            Order = WFPilotOrder.Hold,
            Skill = roster.Skill,
        };
    }

    private void OnMemberState(Entity<WFShipRosterMemberComponent> mob, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Lost(mob);
    }

    private void OnMemberTerminating(Entity<WFShipRosterMemberComponent> mob, ref EntityTerminatingEvent args)
    {
        Lost(mob);
    }

    private void Lost(Entity<WFShipRosterMemberComponent> mob)
    {
        if (!TryComp<WFShipRosterComponent>(mob.Comp.Grid, out var roster)
            || !roster.Slots.TryGetValue(mob.Comp.Slot, out var slot)
            || slot.Occupant != mob.Owner
            || slot.Down != null)
            return;

        slot.Down = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        _accumulator = 0f;
        var now = _timing.CurTime;
        UpdateRelieved(now);

        var query = EntityQueryEnumerator<WFShipRosterComponent>();
        while (query.MoveNext(out var uid, out var roster))
        {
            UpdateAutoRepair((uid, roster), now);
            if (!roster.AutoRestaff)
                continue;

            roster.Restaffs.RemoveAll(t => now - t >= Hour);
            foreach (var slot in roster.Slots.Values.ToList())
            {
                if (roster.Restaffs.Count >= roster.RestaffsPerHour)
                    break;

                if (slot.Down is not { } down || now - down < roster.RestaffDelay || Closed(slot))
                    continue;

                if (SpawnInto((uid, roster), slot, atPost: false))
                {
                    roster.Restaffs.Add(now);
                    _adminLog.Add(LogType.Action, LogImpact.Low, $"Post {slot.Id} on {ToPrettyString(uid):grid} restaffed automatically");
                }
            }
        }
    }

    /// <summary>
    /// With <see cref="WFShipRosterComponent.AutoRepair"/>, every <see cref="WFShipRosterComponent.RepairInterval"/> a
    /// crew with nothing queued is given a repair order: its deckhands and engineers rebuild what's missing from the
    /// ship's repair record and mend what's damaged, and the order ends at once when there's nothing to do.
    /// </summary>
    private void UpdateAutoRepair(Entity<WFShipRosterComponent> grid, TimeSpan now)
    {
        if (!grid.Comp.AutoRepair || now < grid.Comp.NextRepairCheck)
            return;

        grid.Comp.NextRepairCheck = now + grid.Comp.RepairInterval;
        if (_objectives.TryGetQueue(grid, grid.Comp.Group, out var queued) && queued.Count > 0)
            return;

        _objectives.SetQueue(grid, grid.Comp.Group,
            new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Repair } }, append: true);
    }

    /// <summary>Relieved crew leave the ship once they reach the crew bay, or when their time runs out.</summary>
    private void UpdateRelieved(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFShipRelievedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var relieved, out var xform))
        {
            if (now >= relieved.Deadline
                || relieved.Bay is { } bay && _transform.InRange(xform.Coordinates, bay, 1.5f))
                QueueDel(uid);
        }
    }
}
