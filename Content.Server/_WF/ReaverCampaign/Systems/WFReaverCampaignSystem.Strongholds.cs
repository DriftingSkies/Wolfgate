using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.SectorControl;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How far from the origin the first stronghold and a regrouped one are founded.</summary>
    private const float FirstMinDistance = 14000f;
    private const float FirstMaxDistance = 24000f;

    /// <summary>How far from any living player the first stronghold and a regrouped one are founded.</summary>
    private const float PlayerClearance = 8000f;

    /// <summary>Space free of grids around a stronghold's centre.</summary>
    private const float GridClearance = 600f;

    /// <summary>Distance kept from stations by the first stronghold and by later ones.</summary>
    private const float FirstStationClearance = 6000f;
    private const float LaterStationClearance = 3000f;

    /// <summary>How long after its founding a stronghold may send its first patrol.</summary>
    private static readonly TimeSpan FirstPatrolDelay = TimeSpan.FromMinutes(2);

    /// <summary>Founds the first stronghold, a regrouped one or a later one, as the round calls for.</summary>
    private void TickFounding(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var standing = Strongholds();
        if (standing.Count == 0)
        {
            if (state.Founded > 0)
                state.NoStrongholdSince ??= now;

            if (FoundingDue(campaign, state) is { } due && now >= due && now >= _nextFoundTry && !TryFound(out _))
                _nextFoundTry = now + FoundRetry;
            return;
        }

        state.NoStrongholdSince = null;
        if (standing.Count >= campaign.MaxStrongholds
            || _territory.Held(_territory.SectorMap, campaign.Faction).Count < campaign.CellsPerStronghold * standing.Count
            || state.LastFounded is { } last && now < last + campaign.FoundInterval
            || now < _nextFoundTry)
            return;

        if (!TryFound(out _))
            _nextFoundTry = now + FoundRetry;
    }

    /// <summary>When a founding is due with none standing: the first after the stations generate, a regroup after the last stronghold fell; null if that clock has not started.</summary>
    public TimeSpan? FoundingDue(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent? state)
    {
        if (state == null || state.Founded == 0)
            return _stationsAt is { } at ? at + campaign.FirstStrongholdDelay : null;

        return state.NoStrongholdSince is { } since ? since + campaign.RegroupDelay : null;
    }

    /// <summary>Founds a stronghold where the rules put it: a quiet cell far out with none standing, otherwise a held frontier cell.</summary>
    public bool TryFound(out EntityUid stronghold)
    {
        stronghold = default;
        if (Campaign is not { } campaign || State(true) is not { } state)
            return false;

        var standing = Strongholds();
        if (standing.Count == 0)
        {
            return TryFirstCell(campaign, out var first)
                && TryFoundAt(_territory.Centre(_territory.SectorMap, first), out stronghold, campaign.StrongholdLight);
        }

        return TryLaterCell(campaign, standing, out var later)
            && TryFoundAt(_territory.Centre(_territory.SectorMap, later), out stronghold, PickStronghold(campaign, state.Tier));
    }

    /// <summary>Founds a stronghold at a point, claims its cell and ring and announces it.</summary>
    public bool TryFoundAt(MapCoordinates at, out EntityUid stronghold, ProtoId<WFEncounterPrototype>? prototype = null)
    {
        stronghold = default;
        if (Campaign is not { } campaign || State(true) is not { } state
            || !_prototypes.TryIndex(prototype ?? PickStronghold(campaign, state.Tier), out var encounterPrototype)
            || encounterPrototype.Ships.Count == 0)
            return false;

        var regroup = state.Founded > 0 && Strongholds().Count == 0;
        if (!_encounters.TrySpawn(encounterPrototype, at, out var uid))
            return false;

        var now = _timing.CurTime;
        var cell = _territory.CellOf(at);
        var comp = AddComp<WFReaverStrongholdComponent>(uid);
        comp.Cell = cell;
        comp.Tier = state.Tier;
        comp.Lead = encounterPrototype.Ships[0].Key;
        comp.NextPatrol = now + FirstPatrolDelay;

        ClaimCells(campaign, at.MapId, cell, uid);
        state.Founded++;
        state.LastFounded = now;
        state.NoStrongholdSince = null;
        Recompute();
        AnnouncePlace(campaign, regroup ? "wf-reaver-regrouped" : "wf-reaver-founded", true, at.MapId, at.Position,
            ("callsign", WFSectorHex.Callsign(cell)),
            ("bounty", ByTier(campaign.StrongholdBounty, state.Tier, 0)));
        stronghold = uid;
        return true;
    }

    /// <summary>Claims a stronghold's cell and ring for it; a station cell in the ring is contested instead.</summary>
    private void ClaimCells(WFReaverCampaignPrototype campaign, MapId map, WFSectorCell cell, EntityUid stronghold)
    {
        var cells = new List<WFSectorCell> { cell };
        cells.AddRange(WFSectorHex.Ring(cell, 1));
        foreach (var claimed in cells)
        {
            if (_territory.Owner(map, claimed) == null && _territory.StationsIn(map, claimed) is { Count: > 0 } stations)
                TryBlockade(campaign, map, claimed, stronghold, stations[0]);
            else
                _territory.TryClaim(map, claimed, campaign.Faction, stronghold);
        }
    }

    /// <summary>Claims each standing stronghold's cells again after sector control was switched back on or resized.</summary>
    private void OnSectorReset(ref WFSectorResetEvent args)
    {
        if (Campaign is not { } campaign)
            return;

        var map = _territory.SectorMap;
        foreach (var (uid, comp, encounter) in Strongholds())
        {
            comp.Cell = _territory.CellOf(new MapCoordinates(Position(encounter, comp.Lead), map));
            ClaimCells(campaign, map, comp.Cell, uid);
        }
    }

    /// <summary>The stronghold for a tier: light at low tiers or with few players, heavy at high tiers with many.</summary>
    private ProtoId<WFEncounterPrototype> PickStronghold(WFReaverCampaignPrototype campaign, int tier)
    {
        var players = Players();
        if (tier >= campaign.HeavyTier && players >= campaign.HeavyPlayers)
            return campaign.StrongholdHeavy;
        if (tier >= campaign.MidTier && players >= campaign.MidPlayers)
            return campaign.StrongholdMid;
        return campaign.StrongholdLight;
    }

    /// <summary>A random quiet, claimable cell 14 to 24 km out, its centre clear and 8 km from any living player.</summary>
    private bool TryFirstCell(WFReaverCampaignPrototype campaign, out WFSectorCell cell)
    {
        cell = default;
        var map = _territory.SectorMap;
        var quietSince = _timing.CurTime - TimeSpan.FromMinutes(campaign.QuietMinutes);
        var players = PlayerPositions(map);
        var rings = (int) (FirstMaxDistance / (_territory.CellSize * 1.5f)) + 2;
        var candidates = new List<WFSectorCell>();
        foreach (var candidate in WFSectorHex.Within(WFSectorCell.Origin, rings))
        {
            var centre = WFSectorHex.Centre(candidate, _territory.CellSize);
            var distance = centre.Length();
            if (distance < FirstMinDistance || distance > FirstMaxDistance
                || _territory.Owner(map, candidate) != null || _territory.ContestOf(map, candidate) != null
                || !_activity.IsQuiet(map, candidate, quietSince)
                || players.Any(player => Vector2.DistanceSquared(player, centre) < PlayerClearance * PlayerClearance)
                || !_territory.CanClaim(map, candidate, campaign.Faction))
                continue;

            candidates.Add(candidate);
        }

        _random.Shuffle(candidates);
        foreach (var candidate in candidates)
        {
            if (!_scheduler.IsClearSpace(map, WFSectorHex.Centre(candidate, _territory.CellSize), GridClearance, FirstStationClearance))
                continue;

            cell = candidate;
            return true;
        }

        return false;
    }

    /// <summary>A held cell on the edge of held space, two cells from every stronghold, with a clear centre; nearest the core first.</summary>
    private bool TryLaterCell(WFReaverCampaignPrototype campaign, List<Entity<WFReaverStrongholdComponent, WFEncounterComponent>> standing,
        out WFSectorCell cell)
    {
        cell = default;
        var map = _territory.SectorMap;
        var held = _territory.Held(map, campaign.Faction).ToHashSet();
        var candidates = new List<WFSectorCell>();
        foreach (var candidate in held)
        {
            var edge = false;
            for (var direction = 0; direction < 6; direction++)
            {
                if (!held.Contains(candidate.Neighbour(direction)))
                    edge = true;
            }

            if (edge && standing.All(other => WFSectorHex.Distance(other.Comp1.Cell, candidate) >= 2))
                candidates.Add(candidate);
        }

        _random.Shuffle(candidates);
        foreach (var candidate in candidates.OrderBy(entry => WFSectorHex.Distance(entry, WFSectorCell.Origin)))
        {
            if (!_scheduler.IsClearSpace(map, WFSectorHex.Centre(candidate, _territory.CellSize), GridClearance, LaterStationClearance))
                continue;

            cell = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Where the living, non-ghost players on a map are.</summary>
    private List<Vector2> PlayerPositions(MapId map)
    {
        var positions = new List<Vector2>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                positions.Add(_transform.GetWorldPosition(xform));
        }

        return positions;
    }

    /// <summary>Breaks strongholds whose lead is out of the fight, counts hits, marks the ships that land them, times support and recalls a patrol once one is attacked.</summary>
    private void TendStrongholds(WFReaverCampaignComponent state, HashSet<EntityUid> ships, float elapsed)
    {
        foreach (var stronghold in Strongholds())
        {
            var (uid, comp, encounter) = stronghold;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                _encounters.Resolve(uid, WFEncounterResolution.Destroyed);
                continue;
            }

            var hits = encounter.ShipHits.Values.Sum();
            if (hits > comp.Hits)
            {
                comp.LastHit = _timing.CurTime;
                MarkAttackers(comp, encounter);
            }

            comp.Hits = hits;
            if (comp.Hits == 0)
                continue;

            if (_timing.CurTime - comp.LastHit <= SupportWindow)
                TrackSupport(comp, encounter, _transform.GetWorldPosition(lead.Grid), ships, elapsed);
            if (!comp.Recalled)
                comp.Recalled = TryRecallPatrol(stronghold);
        }
    }

    /// <summary>Frees a stronghold's cells; broken, it pays its bounty and is announced.</summary>
    private void StrongholdResolved(Entity<WFReaverStrongholdComponent> stronghold, WFEncounterResolution resolution)
    {
        var (uid, comp) = stronghold;
        var state = State();
        var campaign = CampaignFor(state);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
        {
            _territory.Release(uid);
            AfterStrongholdGone(state);
            return;
        }

        var (bounty, paid) = PayStrongholdBounty(campaign, state, (uid, comp, encounter));
        var freed = _territory.Release(uid);
        state.Broken++;
        AfterStrongholdGone(state);
        var position = Position(encounter, comp.Lead);
        AnnouncePlace(campaign, "wf-reaver-broken", false, Transform(uid).MapID, position,
            ("callsign", WFSectorHex.Callsign(comp.Cell)),
            ("bounty", bounty),
            ("paid", paid),
            ("cells", freed));
    }

    /// <summary>Starts the regroup clock when the last stronghold goes, and rescores.</summary>
    private void AfterStrongholdGone(WFReaverCampaignComponent? state)
    {
        if (state == null)
            return;

        if (Strongholds().Count == 0 && state.Founded > 0)
            state.NoStrongholdSince ??= _timing.CurTime;

        if (Campaign != null)
            Recompute();
    }
}
