using System.Linq;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.SectorControl;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ReaverCampaign.Systems;

public sealed partial class WGReaverCampaignSystem
{
    /// <summary>How near the challenged cell's centre a response pack closes before it attacks.</summary>
    private const float ResponseApproach = 600f;

    /// <summary>How long a response pack with no challenger ship to attack holds the cell, in seconds.</summary>
    private const float ResponseHold = 600f;

    /// <summary>The response packs out on the sector map.</summary>
    public List<Entity<WGReaverResponseComponent, WGEncounterComponent>> Responses()
    {
        var map = _territory.SectorMap;
        var responses = new List<Entity<WGReaverResponseComponent, WGEncounterComponent>>();
        var query = EntityQueryEnumerator<WGReaverResponseComponent, WGEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var response, out var encounter, out var xform))
        {
            if (encounter.Resolution == null && xform.MapID == map && !TerminatingOrDeleted(uid))
                responses.Add((uid, response, encounter));
        }

        return responses;
    }

    /// <summary>Answers another faction challenging a Reaver cell with a response pack from the nearest stronghold, one per cell.</summary>
    private void OnCellContested(ref WGSectorCellContestedEvent args)
    {
        if (args.Holder is not { } holder || args.Map != _territory.SectorMap || !Running || Held
            || Campaign is not { } campaign || holder != campaign.Faction || args.Faction == campaign.Faction)
            return;

        TryRespond(args.Cell, out _);
    }

    /// <summary>
    /// Sends a response pack from the stronghold nearest a challenged Reaver cell: to the cell, at the challenger's lead,
    /// then home. Announces it. False with no challenge on the cell, a pack already defending it, or no stronghold.
    /// </summary>
    public bool TryRespond(WGSectorCell cell, out EntityUid pack)
    {
        pack = default;
        var map = _territory.SectorMap;
        if (Campaign is not { } campaign || State(true) is not { Finished: false }
            || _territory.ContestOf(map, cell) is not { Holder: { } holder } contest || holder != campaign.Faction
            || Responses().Any(response => response.Comp1.Cell == cell && !response.Comp1.Returning)
            || !_prototypes.TryIndex(campaign.ResponsePack, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var standing = Strongholds();
        if (standing.Count == 0)
            return false;

        var centre = WGSectorHex.Centre(cell, _territory.CellSize);
        if (!TryLaunchFrom(prototype, standing, map, centre, out var launched, out var from))
            return false;

        var (uid, encounter) = launched;
        var lead = prototype.Ships[0].Key;
        var home = Position(encounter, lead);
        var target = contest.Source is { } source && ChallengerLead(source) is { } grid ? (EntityUid?) grid : null;
        var orders = new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.GoTo, Position = centre, Range = ResponseApproach },
            target is { } attack
                ? new WGCrewObjective { Kind = WGCrewObjectiveKind.Attack, Target = GetNetEntity(attack) }
                : new WGCrewObjective { Kind = WGCrewObjectiveKind.Hold, Duration = ResponseHold },
            new() { Kind = WGCrewObjectiveKind.GoTo, Position = home, Range = RouteRange },
        };
        if (!_encounters.SetOrders(uid, lead, orders))
        {
            _encounters.End(uid);
            return false;
        }

        var comp = AddComp<WGReaverResponseComponent>(uid);
        comp.Cell = cell;
        comp.Target = target != null ? contest.Source : null;
        comp.Stronghold = from.Owner;
        comp.Lead = lead;
        comp.Home = home;
        Log.Info($"Reaver response pack {ToPrettyString(uid)} launched from {ToPrettyString(from.Owner)} for {WGSectorHex.Callsign(cell)}.");
        pack = uid;
        return true;
    }

    /// <summary>The lead hull of a challenger's encounter, if it is still out.</summary>
    private EntityUid? ChallengerLead(EntityUid source)
    {
        if (!TryComp<WGEncounterComponent>(source, out var encounter) || encounter.Resolution != null
            || !_prototypes.TryIndex(encounter.Prototype, out var prototype) || prototype.Ships.Count == 0
            || !encounter.Ships.TryGetValue(prototype.Ships[0].Key, out var ship) || TerminatingOrDeleted(ship.Grid))
            return null;

        return ship.Grid;
    }

    /// <summary>Breaks response packs whose lead is out of the fight, and sends home those whose challenger or challenge is gone.</summary>
    private void TendResponses()
    {
        var map = _territory.SectorMap;
        var faction = CampaignFor(State())?.Faction;
        foreach (var response in Responses())
        {
            var (uid, comp, encounter) = response;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                BreakLead(uid, encounter, comp.Lead);
                continue;
            }

            if (comp.Returning)
                continue;

            var targetGone = comp.Target is { } target && ChallengerLead(target) == null;
            var challengeGone = _territory.ContestOf(map, comp.Cell) is not { Holder: { } holder } || holder != faction;
            if (targetGone || challengeGone)
                SendHome(response);
        }
    }

    /// <summary>Turns a response pack for home; it resolves completed once there.</summary>
    private void SendHome(Entity<WGReaverResponseComponent, WGEncounterComponent> response)
    {
        var (uid, comp, _) = response;
        comp.Returning = true;
        _encounters.SetOrders(uid, comp.Lead, new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.GoTo, Position = comp.Home, Range = RouteRange },
        });
        Log.Info($"Reaver response pack {ToPrettyString(uid)} turns for home.");
    }

    /// <summary>Sends home the packs defending a challenged Reaver cell once the challenge is decided, and counts and announces a cell lost.</summary>
    private void OnContestResolved(ref WGSectorContestResolvedEvent args)
    {
        if (args.Holder is not { } holder || args.Map != _territory.SectorMap)
            return;

        var state = State();
        if (CampaignFor(state) is not { } campaign || holder != campaign.Faction)
            return;

        foreach (var response in Responses())
        {
            if (response.Comp1.Cell == args.Cell && !response.Comp1.Returning)
                SendHome(response);
        }

        if (!args.Claimed || state == null)
            return;

        state.CellsLost++;
        AnnounceRadio(Loc.GetString("wg-reaver-cell-lost",
            ("callsign", WGSectorHex.Callsign(args.Cell)),
            ("faction", FactionName(args.Faction))));
        Recompute();
    }

    /// <summary>A faction's display name.</summary>
    private string FactionName(ProtoId<WGSectorFactionPrototype> faction)
    {
        return _prototypes.TryIndex(faction, out var prototype) ? Loc.GetString(prototype.Name) : faction.Id;
    }

    /// <summary>A response pack destroyed by players pays the patrol bounty and is announced.</summary>
    private void ResponseResolved(Entity<WGReaverResponseComponent> response, WGEncounterResolution resolution)
    {
        var (uid, comp) = response;
        var state = State();
        if (resolution != WGEncounterResolution.Destroyed || CampaignFor(state) is not { } campaign || state == null)
            return;

        var (bounty, paid) = PayPatrolBounty(campaign, uid);
        AnnounceRadio(Loc.GetString("wg-reaver-response-destroyed",
            ("callsign", WGSectorHex.Callsign(comp.Cell)),
            ("bounty", bounty),
            ("paid", paid)));
    }
}
