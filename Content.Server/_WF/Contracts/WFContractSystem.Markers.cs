using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Waypoints;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Contracts;
using Robust.Shared.Map;

namespace Content.Server._WF.Contracts;

/// <summary>
/// Keeping contracts out of each other's way. A bounty's ships and a salvage wreck carry the holder's name on every
/// radar, so others can see the work is claimed; the holder's crew get a waypoint for the target on the ships they are
/// aboard; and a player outside the crew whose ship fires on a claimed target loses standing with the contractor, unless
/// they are on the holder's side or the contractor's (TSF helping TSF is not interference).
/// </summary>
public sealed partial class WFContractSystem
{
    [Dependency] private WFWaypointSystem _waypoints = default!;

    /// <summary>Standing lost by a player outside the crew who fired on a claimed target.</summary>
    private const int InterferencePenalty = 2;

    /// <summary>Labels a bounty's ships, or a wreck, with who holds the contract; ships spawned late are labelled when they appear.</summary>
    private void Claim(WFContract contract)
    {
        var claimant = Claimant(contract);
        if (contract.WreckGrid is { } wreck && contract.Claimed.Add(wreck) && !TerminatingOrDeleted(wreck))
            _meta.SetEntityName(wreck, Loc.GetString("wf-contract-claimed-name", ("name", Name(wreck)), ("claimant", claimant)));

        if (_proto.Index(contract.Prototype).Objective is not WFContractBounty
            || contract.EncounterEntity is not { } encounter || !TryComp<WFEncounterComponent>(encounter, out var comp))
            return;

        foreach (var ship in comp.Ships.Values)
        {
            if (ship.Virtual || TerminatingOrDeleted(ship.Grid) || !contract.Claimed.Add(ship.Grid))
                continue;

            _meta.SetEntityName(ship.Grid, Loc.GetString("wf-contract-claimed-name", ("name", Name(ship.Grid)), ("claimant", claimant)));
        }
    }

    /// <summary>Who a contract is claimed by: the holder's crew, or the holder.</summary>
    private string Claimant(WFContract contract)
    {
        return contract.User is { } user && _crews.CrewNameOf(user) is { } crew ? crew : contract.Character;
    }

    /// <summary>Where a live contract's target is now, if it has one in space: a bounty's ship, an escort, a wreck, or cargo to collect.</summary>
    private MapCoordinates? MarkerPosition(WFContract contract)
    {
        var proto = _proto.Index(contract.Prototype);
        switch (proto.Objective)
        {
            case WFContractBounty:
            case WFContractEscort:
                return TargetPosition(contract) is { } target && contract.Site is { } site ? new MapCoordinates(target, site.MapId) : contract.Site;
            case WFContractRecovery:
                return contract.WreckGrid is { } wreck && !TerminatingOrDeleted(wreck) ? _transform.GetMapCoordinates(wreck) : contract.Site;
            case WFContractCourier when contract.Origin is { } pickup:
                return contract.Items.FirstOrDefault(i => !TerminatingOrDeleted(i) && OnStation(i, pickup)) is { Valid: true } waiting
                    ? _transform.GetMapCoordinates(waiting)
                    : null;
        }

        return null;
    }

    /// <summary>
    /// Puts or moves a waypoint for each live contract's target on every player ship the holder's crew is aboard. A
    /// waypoint the crew deleted is not put back.
    /// </summary>
    private void UpdateMarkers()
    {
        foreach (var contract in _contracts.Values)
        {
            if (contract.Status != WFContractStatus.Active || contract.User is not { } user)
                continue;

            Claim(contract);
            if (MarkerPosition(contract) is not { } at)
                continue;

            var name = Loc.GetString("wf-contract-marker-name", ("id", contract.Id));
            var bodies = _crews.CrewInRound(user).Select(member => member.Body).ToList();
            if (MobOf(new WFContractHolder(user, contract.Character)) is { } holder)
                bodies.Add(holder);

            foreach (var body in bodies)
            {
                if (Transform(body).GridUid is not { } ship || !HasComp<ShuttleDeedComponent>(ship))
                    continue;

                if (contract.Markers.TryGetValue(ship, out var id) && id == null)
                    continue;

                contract.Markers[ship] = _waypoints.Place(ship, id, name, at);
            }
        }
    }

    /// <summary>The side a company is on: its contract faction, else the company itself; none for no company.</summary>
    private string? SideOf(string? company)
    {
        if (string.IsNullOrEmpty(company) || company == "None")
            return null;

        foreach (var faction in _proto.EnumeratePrototypes<WFContractFactionPrototype>())
        {
            if (faction.Companies.Any(c => c.Id == company))
                return faction.ID;
        }

        return company;
    }

    /// <summary>Takes down the waypoints a contract put up.</summary>
    private void ClearMarkers(WFContract contract)
    {
        foreach (var (ship, id) in contract.Markers)
        {
            if (id is { } waypoint && !TerminatingOrDeleted(ship))
                _waypoints.Remove(ship, waypoint);
        }

        contract.Markers.Clear();
    }

    /// <summary>
    /// When a claimed target's encounter ends, every player outside the holder's crew whose ship hit it loses standing
    /// with the contractor, and is told why.
    /// </summary>
    private void ClaimantsInterfered(WFContract contract, EntityUid encounter)
    {
        if (contract.User is not { } user || !TryComp<WFEncounterComponent>(encounter, out var comp))
            return;

        var crew = _crews.CrewMembers(user).ToHashSet();
        var contractor = _proto.Index(contract.Prototype).Contractor;
        var friends = new HashSet<string>();
        if (SideOf(contract.Company) is { } holderSide)
            friends.Add(holderSide);
        foreach (var company in _proto.Index(contractor).Companies)
        {
            if (SideOf(company.Id) is { } side)
                friends.Add(side);
        }

        foreach (var (attacker, hits) in comp.Hits)
        {
            if (crew.Contains(attacker) || hits.Values.Sum() <= 0
                || !_mind.TryGetMind(attacker, out var mind) || mind.Value.Comp.OwnedEntity is not { } body
                || HolderOf(body) is not { } meddler
                || SideOf(CompOrNull<CompanyComponent>(body)?.CompanyName.Id) is { } side && friends.Contains(side))
                continue;

            AddStanding(meddler, contractor, -InterferencePenalty);
            Tell(meddler, Loc.GetString("wf-contract-interfered", ("contractor", Loc.GetString(_proto.Index(contractor).Name)),
                ("claimant", Claimant(contract)), ("points", InterferencePenalty)));
        }
    }
}
