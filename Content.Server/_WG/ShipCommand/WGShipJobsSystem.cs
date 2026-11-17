using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server._NF.CryoSleep;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's player jobs (<see cref="WGShipJobsComponent"/>): opening and closing their slots, listing the
/// ship as a late-join station only while one is open, and handing a post between its NPC and the player who takes
/// the job. A job taken relieves the matching crewman or trader and raises the player to the job's command list, never
/// lowering a standing or lifting a bar (the barred are turned away from the jobs); cryosleep, a deleted body or a
/// release hands the post back to the roster, puts a raised standing back, and reopens the slot.
/// </summary>
public sealed class WGShipJobsSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private StationJobsSystem _jobs = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WGShipAccessSystem _access = default!;
    [Dependency] private WGShipCommandSystem _command = default!;
    [Dependency] private WGShipRosterSystem _roster = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<WGShipJobHolderComponent, CryosleepEnterEvent>(OnCryosleep);
        SubscribeLocalEvent<WGShipJobHolderComponent, EntityTerminatingEvent>(OnHolderTerminating);
    }

    /// <summary>Whether a station is a commanded ship with player jobs, which joins list only while a job is open.</summary>
    public bool IsJobsStation(EntityUid station)
    {
        return TryGetShip(station, out _);
    }

    /// <summary>
    /// Whether a commanded ship's station goes on the join list: only while one of its jobs has an open slot. The join
    /// list shows it as a station, with its open jobs only.
    /// </summary>
    public bool ListedForJoin(EntityUid station, StationJobsComponent jobs)
    {
        return IsJobsStation(station) && jobs.JobList.Values.Any(open => open != 0);
    }

    /// <summary>The commanded ship grid of a station with player jobs.</summary>
    public bool TryGetShip(EntityUid station, [NotNullWhen(true)] out Entity<WGShipJobsComponent>? ship)
    {
        ship = null;
        if (!TryComp<StationDataComponent>(station, out var data))
            return false;

        foreach (var grid in data.Grids)
        {
            if (TryComp<WGShipJobsComponent>(grid, out var jobs))
            {
                ship = (grid, jobs);
                return true;
            }
        }

        return false;
    }

    /// <summary>The open slots of each of a ship's jobs; empty when the ship has no station yet.</summary>
    public Dictionary<ProtoId<JobPrototype>, int> GetSlots(Entity<WGShipJobsComponent> ship)
    {
        var slots = new Dictionary<ProtoId<JobPrototype>, int>();
        if (_station.GetOwningStation(ship) is not { } station)
            return slots;

        foreach (var job in ship.Comp.Jobs.Keys)
        {
            _jobs.TryGetJobSlot(station, job, out var open);
            slots[job] = open ?? 0;
        }

        return slots;
    }

    /// <summary>
    /// Sets how many players may still take one of a ship's jobs; 0 closes it. False when the ship has no such job or
    /// no station.
    /// </summary>
    public bool SetSlots(Entity<WGShipJobsComponent> ship, ProtoId<JobPrototype> job, int count, EntityUid? actor)
    {
        if (!ship.Comp.Jobs.ContainsKey(job) || count < 0 || _station.GetOwningStation(ship) is not { } station)
            return false;

        if (!_jobs.TrySetJobSlot(station, job, count, createSlot: true))
            return false;

        if (count == 0)
            ship.Comp.Closed.Add(job);
        else
            ship.Comp.Closed.Remove(job);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):actor} set {job} on {ToPrettyString(ship.Owner):grid} to {count} open slots");
        return true;
    }

    /// <summary>The players holding a ship's jobs now.</summary>
    public List<Entity<WGShipJobHolderComponent>> Holders(EntityUid grid)
    {
        var holders = new List<Entity<WGShipJobHolderComponent>>();
        var query = EntityQueryEnumerator<WGShipJobHolderComponent>();
        while (query.MoveNext(out var uid, out var holder))
        {
            if (holder.Grid == grid)
                holders.Add((uid, holder));
        }

        return holders;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (ev.JobId is not { } jobId
            || !TryGetShip(ev.Station, out var found)
            || !found.Value.Comp.Jobs.TryGetValue(jobId, out var job))
            return;

        var ship = found.Value;
        var holder = EnsureComp<WGShipJobHolderComponent>(ev.Mob);
        holder.Grid = ship;
        holder.Station = ev.Station;
        holder.Job = jobId;
        holder.User = ev.Player.UserId;

        if (job.Standing is { } list && TryComp<WGShipCommandComponent>(ship, out var command))
        {
            WGShipAccessKey? key = _access.TryGetCard(ev.Mob, out var card) && _access.TryGetKey(card, out var cardKey) ? cardKey : null;
            // A job raises a standing, never lifts a bar or lowers one: a barred player who got in kept their bar, and an
            // officer on a Staff job stays an officer. A lower standing comes back when they leave.
            var before = WGShipCommandSystem.FindListed(command, key, ev.Player.UserId, out var beforeList);
            if ((before == null || beforeList != WGShipCommandList.Barred && Rank(beforeList) < Rank(list))
                && _command.AddPerson((ship, command), list, key, ev.Player.UserId, Name(ev.Mob)))
            {
                holder.Standing = list;
                if (before != null)
                    holder.Restore = (beforeList, before);
            }
        }

        if (TryComp<WGShipRosterComponent>(ship, out var roster) && PickSlot(roster, job) is { } slot)
        {
            holder.Slot = slot.Id;
            _roster.Hold((ship, roster), slot, ev.Mob);
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(ev.Mob):player} took the {jobId} job on {ToPrettyString(ship.Owner):grid}{(holder.Slot != null ? $", relieving post {holder.Slot}" : "")}");
    }

    /// <summary>How high a command list stands, for a job that may only raise someone.</summary>
    private static int Rank(WGShipCommandList list) => list switch
    {
        WGShipCommandList.Officers => 2,
        WGShipCommandList.Staff => 1,
        _ => 0,
    };

    /// <summary>Whether a commanded ship turns a player away from its jobs: they are on its barred list.</summary>
    public bool Refuses(EntityUid station, NetUserId user)
    {
        return TryGetShip(station, out var ship)
               && TryComp<WGShipCommandComponent>(ship, out var command)
               && WGShipCommandSystem.FindListed(command, null, user, out var list) != null
               && list == WGShipCommandList.Barred;
    }

    /// <summary>The post a job takes over: one whose NPC is on duty first, then an empty one, then one stood down.</summary>
    private WGShipRosterSlot? PickSlot(WGShipRosterComponent roster, WGShipJob job)
    {
        var matching = roster.Slots.Values
            .Where(s => s.Holder == null
                        && (job.Role is { } role && s.Kind == WGShipRosterKind.Crew && s.Role == role
                            || job.Counter is { } trader && s.Kind == WGShipRosterKind.Counter && s.Trader == trader))
            .ToList();

        return matching.FirstOrDefault(s => !s.StoodDown && _roster.Held(s))
               ?? matching.FirstOrDefault(s => !s.StoodDown)
               ?? matching.FirstOrDefault();
    }

    private void OnCryosleep(Entity<WGShipJobHolderComponent> holder, ref CryosleepEnterEvent args)
    {
        Leave(holder);
    }

    private void OnHolderTerminating(Entity<WGShipJobHolderComponent> holder, ref EntityTerminatingEvent args)
    {
        Leave(holder);
    }

    /// <summary>
    /// A player gives up a ship's job: the post goes back to the roster, which restaffs it by its usual rule; the
    /// slot reopens; and they come off the command list the job put them on.
    /// </summary>
    public void Leave(Entity<WGShipJobHolderComponent> holder)
    {
        var comp = holder.Comp;
        RemComp<WGShipJobHolderComponent>(holder);

        if (comp.Slot is { } slotId
            && TryComp<WGShipRosterComponent>(comp.Grid, out var roster)
            && roster.Slots.TryGetValue(slotId, out var slot)
            && slot.Holder == holder.Owner)
            _roster.Release((comp.Grid, roster), slot);

        // Off the job's list, and back on the one they stood on before; not if someone has moved them since.
        if (comp.Standing is { } list && TryComp<WGShipCommandComponent>(comp.Grid, out var command)
            && _command.RemovePerson((comp.Grid, command), list, null, comp.User)
            && comp.Restore is { } restore)
            _command.AddPerson((comp.Grid, command), restore.List, restore.Person.Key, restore.Person.User, restore.Person.Name);

        if (!TerminatingOrDeleted(comp.Station) && HasComp<StationJobsComponent>(comp.Station))
        {
            if (_jobs.TryGetPlayerJobs(comp.Station, comp.User, out var jobs) && jobs.Contains(comp.Job))
                _jobs.TryRemovePlayerJobs(comp.Station, comp.User);
            if (!TryComp<WGShipJobsComponent>(comp.Grid, out var ship) || !ship.Closed.Contains(comp.Job))
                _jobs.TryAdjustJobSlot(comp.Station, comp.Job, 1, clamp: true);
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(holder.Owner):player} left the {comp.Job} job on {ToPrettyString(comp.Grid):grid}");
    }
}
