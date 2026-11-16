using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipTiers;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipTiers;

/// <summary>
/// Caps each fighting faction's capitals by its members online, keeps a new capital grounded while it is built and
/// locks a lost capital's slot for a while.
/// </summary>
public sealed class WFShipTierSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protos = default!;

    private static readonly ProtoId<WFShipTiersPrototype> Rules = "Default";

    /// <summary>When each faction's locked capital slots open again.</summary>
    private readonly Dictionary<string, List<TimeSpan>> _lockouts = new();

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AttemptShipyardShuttlePurchaseEvent>(OnAttemptPurchase);
        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnPurchased);
        SubscribeLocalEvent<WFCapitalShipComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _lockouts.Clear());
    }

    /// <summary>Capital slots the faction may fill with this many members online.</summary>
    public static int SlotsFor(WFShipTiersPrototype rules, int members)
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
        var query = EntityQueryEnumerator<WFCapitalShipComponent>();
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
            return;

        ev.CancelReason = "wf-ship-tiers-capital-cap";
        ev.Cancel();
    }

    private void OnPurchased(ShipyardShuttlePurchaseEvent ev)
    {
        if (!_protos.TryIndex(Rules, out var rules)
            || !TryComp<VesselComponent>(ev.Shuttle, out var vessel)
            || !rules.Capitals.Contains(vessel.VesselId)
            || !TryComp<CompanyComponent>(ev.Shuttle, out var shipCompany)
            || !rules.Companies.Contains(shipCompany.CompanyName))
            return;

        var capital = EnsureComp<WFCapitalShipComponent>(ev.Shuttle);
        capital.Company = shipCompany.CompanyName;
        capital.ReadyAt = _timing.CurTime + rules.BuildTime;
        EnsureComp<PreventPilotComponent>(ev.Shuttle);

        _chat.DispatchGlobalAnnouncement(Loc.GetString("wf-ship-tiers-construction-started",
                ("faction", FactionName(shipCompany.CompanyName)),
                ("ship", Name(ev.Shuttle)),
                ("minutes", (int) rules.BuildTime.TotalMinutes)),
            Loc.GetString("wf-ship-tiers-announcer"), colorOverride: Color.Orange);
    }

    private void OnTerminating(Entity<WFCapitalShipComponent> ent, ref EntityTerminatingEvent args)
    {
        // A sale deletes the ship while it is docked; anything else that deletes it is a loss.
        if (!ent.Comp.Lost && !IsDocked(ent))
            Lose(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<WFCapitalShipComponent>();
        while (query.MoveNext(out var uid, out var capital))
        {
            if (capital.ReadyAt is { } ready)
            {
                if (_timing.CurTime < ready)
                    continue;

                capital.ReadyAt = null;
                RemComp<PreventPilotComponent>(uid);
                _chat.DispatchGlobalAnnouncement(Loc.GetString("wf-ship-tiers-construction-done",
                        ("faction", FactionName(capital.Company)),
                        ("ship", Name(uid))),
                    Loc.GetString("wf-ship-tiers-announcer"), colorOverride: Color.Orange);
                continue;
            }

            // A wreck adrift: Mono's activity check finds no powered consoles and nobody is docking it home.
            if (!capital.Lost
                && TryComp<ShipActivityComponent>(uid, out var activity)
                && activity.InactivePastThreshold
                && !IsDocked(uid))
            {
                Lose((uid, capital));
            }
        }
    }

    /// <summary>Marks the capital lost and locks its faction's slot.</summary>
    private void Lose(Entity<WFCapitalShipComponent> ent)
    {
        if (!_protos.TryIndex(Rules, out var rules))
            return;

        ent.Comp.Lost = true;
        var company = ent.Comp.Company.Id;
        if (!_lockouts.TryGetValue(company, out var locks))
            _lockouts[company] = locks = new List<TimeSpan>();
        locks.Add(_timing.CurTime + rules.Lockout);

        _chat.DispatchGlobalAnnouncement(Loc.GetString("wf-ship-tiers-capital-lost",
                ("faction", FactionName(ent.Comp.Company)),
                ("ship", Name(ent)),
                ("minutes", (int) rules.Lockout.TotalMinutes)),
            Loc.GetString("wf-ship-tiers-announcer"), colorOverride: Color.Orange);
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
