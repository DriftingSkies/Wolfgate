using Content.Server._WG.Shipyard;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.ShipTiers;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipTiers;

/// <summary>
/// Caps each fighting faction's capitals by its members online, keeps a new capital grounded while it is built and
/// locks a lost capital's slot for a while.
/// </summary>
public sealed class WGShipTierSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protos = default!;

    private static readonly ProtoId<WGShipTiersPrototype> Rules = "WGDefault";

    /// <summary>When each faction's locked capital slots open again.</summary>
    private readonly Dictionary<string, List<TimeSpan>> _lockouts = new();

    /// <summary>
    /// Hulls a capital purchase was checked against the cap for. Only these become capitals: an admin spawn or a used
    /// hull from a trader raises the purchase event without the check, and takes no slot.
    /// </summary>
    /// <summary>Capital purchases admitted under the cap, with the buyer's faction.</summary>
    private readonly Dictionary<EntityUid, ProtoId<CompanyPrototype>> _approved = new();

    private TimeSpan _nextCheck;

    /// <summary>Set from round-restart cleanup until the next round starts, while the old round's grids are flushed.</summary>
    private bool _flushing;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AttemptShipyardShuttlePurchaseEvent>(OnAttemptPurchase);
        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnPurchased);
        SubscribeLocalEvent<WGCapitalShipComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<ShipSoldEvent>(OnSold);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _lockouts.Clear();
            _approved.Clear();
            _flushing = true;
        });
        SubscribeLocalEvent<RoundStartingEvent>(_ => _flushing = false);
    }

    /// <summary>Capital slots the faction may fill with this many members online.</summary>
    public static int SlotsFor(WGShipTiersPrototype rules, int members)
    {
        var slots = 0;
        foreach (var threshold in rules.Thresholds)
        {
            if (members >= threshold.Members)
                slots = Math.Max(slots, threshold.Slots);
        }
        return slots;
    }

    /// <summary>Members of the faction with a player attached.</summary>
    public int MembersOnline(string company)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ActorComponent, CompanyComponent>();
        while (query.MoveNext(out _, out _, out var member))
        {
            if (member.CompanyName == company)
                count++;
        }
        return count;
    }

    /// <summary>Slots the faction is using: capitals not yet lost, plus locked slots.</summary>
    public int SlotsUsed(string company)
    {
        var used = 0;
        var query = EntityQueryEnumerator<WGCapitalShipComponent>();
        while (query.MoveNext(out _, out var capital))
        {
            if (!capital.Lost && capital.Company == company)
                used++;
        }

        if (_lockouts.TryGetValue(company, out var locks))
        {
            locks.RemoveAll(end => end <= _timing.CurTime);
            used += locks.Count;
        }
        return used;
    }

    private void OnAttemptPurchase(ref AttemptShipyardShuttlePurchaseEvent ev)
    {
        if (ev.Cancelled
            || !_protos.TryIndex(Rules, out var rules)
            || !rules.Capitals.Contains(ev.Vessel.ID)
            || !TryComp<CompanyComponent>(ev.Purchaser, out var buyer)
            || !rules.Companies.Contains(buyer.CompanyName))
            return;

        var company = buyer.CompanyName.Id;
        if (SlotsUsed(company) < SlotsFor(rules, MembersOnline(company)))
        {
            _approved[ev.Shuttle] = buyer.CompanyName;
            return;
        }

        ev.CancelReason = "wg-ship-tiers-capital-cap";
        ev.Cancel();
    }

    private void OnPurchased(ShipyardShuttlePurchaseEvent ev)
    {
        if (!_approved.Remove(ev.Shuttle, out var company)
            || !_protos.TryIndex(Rules, out var rules)
            || !TryComp<VesselComponent>(ev.Shuttle, out var vessel)
            || !rules.Capitals.Contains(vessel.VesselId))
            return;

        var capital = EnsureComp<WGCapitalShipComponent>(ev.Shuttle);
        capital.Company = company;
        capital.ReadyAt = _timing.CurTime + rules.BuildTime;
        EnsureComp<PreventPilotComponent>(ev.Shuttle);

        _chat.DispatchGlobalAnnouncement(Loc.GetString("wg-ship-tiers-construction-started",
                ("faction", FactionName(company)),
                ("ship", Name(ev.Shuttle)),
                ("minutes", (int) rules.BuildTime.TotalMinutes)),
            Loc.GetString("wg-ship-tiers-announcer"), colorOverride: Color.Orange);
    }

    private void OnTerminating(Entity<WGCapitalShipComponent> ent, ref EntityTerminatingEvent args)
    {
        // A sold capital and a round restart's flush aren't losses; any other deletion is.
        if (!_flushing && !ent.Comp.Lost && !ent.Comp.Sold)
            Lose(ent);
    }

    private void OnSold(ref ShipSoldEvent ev)
    {
        if (TryComp<WGCapitalShipComponent>(ev.Shuttle, out var capital))
            capital.Sold = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<WGCapitalShipComponent>();
        while (query.MoveNext(out var uid, out var capital))
        {
            if (capital.ReadyAt is { } ready)
            {
                if (_timing.CurTime < ready)
                    continue;

                capital.ReadyAt = null;
                RemComp<PreventPilotComponent>(uid);
                _chat.DispatchGlobalAnnouncement(Loc.GetString("wg-ship-tiers-construction-done",
                        ("faction", FactionName(capital.Company)),
                        ("ship", Name(uid))),
                    Loc.GetString("wg-ship-tiers-announcer"), colorOverride: Color.Orange);
                continue;
            }

            if (!TryComp<ShipActivityComponent>(uid, out var activity))
                continue;

            // Written off for drifting, but crewed and powered again: it takes its slot back and its lockout ends.
            if (capital.Lost)
            {
                if (capital.LockedUntil != null && !activity.InactiveLastCheck)
                    TryRecover((uid, capital));
                continue;
            }

            // Mono's InactivePastThreshold never clears, and a capital sits unpowered while it is built, so the live
            // count is read instead, and only once the ship has flown since construction.
            if (!activity.InactiveLastCheck)
            {
                capital.SeenActive = true;
                continue;
            }

            // A wreck adrift: no powered consoles for the threshold, and nobody docking it home.
            if (capital.SeenActive
                && activity.GetMinutesInactive() >= activity.InactiveThresholdMinutes
                && !IsDocked(uid))
            {
                Lose((uid, capital));
            }
        }
    }

    /// <summary>Marks the capital lost and locks its faction's slot.</summary>
    private void Lose(Entity<WGCapitalShipComponent> ent)
    {
        if (!_protos.TryIndex(Rules, out var rules))
            return;

        ent.Comp.Lost = true;
        ent.Comp.LockedUntil = _timing.CurTime + rules.Lockout;
        var company = ent.Comp.Company.Id;
        if (!_lockouts.TryGetValue(company, out var locks))
            _lockouts[company] = locks = new List<TimeSpan>();
        locks.Add(ent.Comp.LockedUntil.Value);

        _chat.DispatchGlobalAnnouncement(Loc.GetString("wg-ship-tiers-capital-lost",
                ("faction", FactionName(ent.Comp.Company)),
                ("ship", Name(ent)),
                ("minutes", (int) rules.Lockout.TotalMinutes)),
            Loc.GetString("wg-ship-tiers-announcer"), colorOverride: Color.Orange);
    }

    /// <summary>A capital written off for drifting is back in service: it holds its slot again instead of a lockout.</summary>
    /// <summary>Restores a lost capital, unless its slot has gone to a replacement; then it stays lost and grounded.</summary>
    private void TryRecover(Entity<WGCapitalShipComponent> ent)
    {
        var company = ent.Comp.Company.Id;
        var ownLock = ent.Comp.LockedUntil is { } end && end > _timing.CurTime ? 1 : 0;
        if (!_protos.TryIndex(Rules, out var rules)
            || SlotsUsed(company) - ownLock >= SlotsFor(rules, MembersOnline(company)))
        {
            EnsureComp<PreventPilotComponent>(ent);
            return;
        }

        if (ent.Comp.LockedUntil is { } until && _lockouts.TryGetValue(company, out var locks))
            locks.Remove(until);
        RemComp<PreventPilotComponent>(ent);
        ent.Comp.Lost = false;
        ent.Comp.LockedUntil = null;
        ent.Comp.SeenActive = true;
    }

    private bool IsDocked(EntityUid grid)
    {
        return _docking.GetDocks(grid).Any(dock => dock.Comp.Docked);
    }

    private string FactionName(ProtoId<CompanyPrototype> company)
    {
        return _protos.TryIndex(company, out var proto) ? proto.Name : company.Id;
    }
}
