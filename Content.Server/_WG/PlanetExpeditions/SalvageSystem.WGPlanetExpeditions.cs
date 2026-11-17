using System.Linq;
using System.Numerics;
using Content.Server._WG.PlanetExpeditions;
using Content.Server._WG.PlanetPois;
using Content.Shared._WG.Planets;
using Content.Shared._WG.Vehicles.Base;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions.Structure;
using Content.Shared._NF.CCVar;
using Content.Shared._WG.CCVar;
using Content.Shared.Chat;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Station.Components;
using Content.Shared.Storage;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.Salvage;

/// <summary>
/// Expeditions on the sector's worlds: the console offers sites already standing on the planets instead of generating
/// a map, a claim arms the site with the mission's objectives and hostiles, and the crew flies there themselves.
/// </summary>
public sealed partial class SalvageSystem
{
    [Dependency] private WGPlanetPoiSystem _wgPois = default!;
    [Dependency] private SharedRoofSystem _wgRoof = default!;
    [Dependency] private IPlayerManager _wgPlayers = default!;

    private static readonly TimeSpan WgCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long a crew has to reach its site after claiming it, before the mission fails.</summary>
    private static readonly TimeSpan WgTravelTime = TimeSpan.FromMinutes(30);

    /// <summary>How far outside a site's bounds counts as having reached it.</summary>
    private const int WgArrivalMargin = 24;

    /// <summary>How far from the site's middle a crew on the ground hears the mission's announcements.</summary>
    private const float WgAnnounceRange = 96f;

    private TimeSpan _wgNextCheck;

    /// <summary>
    /// How long a cleared site lies quiet before it is offered again. Without it every world ran dry over a long
    /// round but Carcinoma, whose sites nobody clears, and the console offered nothing else.
    /// </summary>
    private static readonly TimeSpan WgSiteRecovery = TimeSpan.FromMinutes(45);

    private void WgInitializePlanetExpeditions()
    {
        SubscribeLocalEvent<WGPlanetExpeditionComponent, ComponentShutdown>(WgOnExpeditionShutdown);
    }

    /// <summary>
    /// Fills the station's offers from free sites on the worlds; false when there are none, so upstream generates
    /// its maps instead.
    /// </summary>
    private bool WgTryOfferPlanetMissions(EntityUid station, SalvageExpeditionDataComponent data)
    {
        if (!_cfgManager.GetCVar(PlanetCVars.PlanetExpeditions))
            return false;

        var free = new List<(EntityUid Ground, int Index)>();
        var query = EntityQueryEnumerator<WGPlanetPoiComponent>();
        while (query.MoveNext(out var ground, out var record))
        {
            // A world still placing its sites has nothing to stand an objective on yet.
            if (!record.NetworkReady)
                continue;

            for (var i = 0; i < record.Sites.Count; i++)
            {
                var site = record.Sites[i];
                if (site.Cleared && _timing.CurTime >= site.ClearedAt + WgSiteRecovery)
                {
                    site.Cleared = false;
                    foreach (var hostile in site.Leftovers)
                    {
                        if (!TerminatingOrDeleted(hostile) && !_mobState.IsDead(hostile))
                            QueueDel(hostile);
                    }

                    site.Leftovers.Clear();
                }

                if (site.Expeditions && site.ExpeditionClaim == null && site.ContractClaim == null && !site.Cleared)
                    free.Add((ground, i));
            }
        }

        if (free.Count == 0)
            return false;

        var offers = EnsureComp<WGPlanetExpeditionComponent>(station);
        offers.Offers.Clear();
        data.Missions.Clear();
        _random.Shuffle(free);

        foreach (var (ground, index) in free.Take(MissionLimit))
        {
            var site = Comp<WGPlanetPoiComponent>(ground).Sites[index];
            var mission = new SalvageMissionParams
            {
                Index = data.NextIndex,
                MissionType = _random.Prob(0.5f) ? SalvageMissionType.Destruction : SalvageMissionType.Elimination,
                Seed = _random.Next(),
                Difficulty = site.Difficulty,
                WgSite = site.Name,
                WgWorld = Name(ground),
            };

            data.Missions[data.NextIndex] = mission;
            offers.Offers[data.NextIndex] = new WGPlanetExpeditionOffer(ground, index);
            data.NextIndex++;
        }

        return true;
    }

    /// <summary>
    /// Claims a site mission in place: no jump, the site is armed and its signal named. False when the mission is
    /// not a site's, so upstream handles it.
    /// </summary>
    private bool WgTryClaimPlanetMission(EntityUid console, SalvageExpeditionConsoleComponent component, EntityUid station, SalvageExpeditionDataComponent data, SalvageMissionParams missionParams)
    {
        if (!TryComp<WGPlanetExpeditionComponent>(station, out var comp) || !comp.Offers.TryGetValue(missionParams.Index, out var offer))
            return false;

        if (!TryComp<WGPlanetPoiComponent>(offer.Ground, out var record)
            || offer.SiteIndex >= record.Sites.Count
            || record.Sites[offer.SiteIndex] is not { ExpeditionClaim: null, ContractClaim: null, Cleared: false } site)
        {
            // Another crew got there first; fresh offers next tick.
            PlayDenySound(console, component);
            _popupSystem.PopupEntity(Loc.GetString("wg-expedition-site-taken"), console, PopupType.MediumCaution);
            data.NextOffer = _timing.CurTime;
            UpdateConsoles(station, data);
            return true;
        }

        var mission = GetMission(missionParams.MissionType, missionParams.Difficulty, missionParams.Seed);
        var hostiles = new List<EntityUid>();
        var objectives = WgArmSite(offer.Ground, site, mission, missionParams, hostiles);

        if (objectives.Count == 0)
        {
            // Nothing to clear would complete, and pay, at once. The site has no room; it is not offered again.
            foreach (var hostile in hostiles)
                QueueDel(hostile);

            site.Expeditions = false;
            Log.Warning($"{site.Name} on {ToPrettyString(offer.Ground)} had no room for an expedition's objectives.");
            PlayDenySound(console, component);
            _popupSystem.PopupEntity(Loc.GetString("wg-expedition-site-unusable"), console, PopupType.MediumCaution);
            data.NextOffer = _timing.CurTime;
            UpdateConsoles(station, data);
            return true;
        }

        var active = new WGPlanetExpeditionActive
        {
            Ground = offer.Ground,
            SiteIndex = offer.SiteIndex,
            Params = missionParams,
            Mission = mission,
            Objectives = objectives,
            Hostiles = hostiles,
            ArriveBy = _timing.CurTime + WgTravelTime,
        };

        site.ExpeditionClaim = station;
        comp.Active = active;
        data.ActiveMission = missionParams.Index;
        data.NextOffer = active.ArriveBy + mission.Duration + TimeSpan.FromSeconds(1);
        data.CanFinish = true;

        if (TryComp<WGPoiSignalComponent>(site.Signal, out var signal) && !signal.Revealed)
            _wgPois.Reveal((site.Signal, signal), null);

        WgAnnounce(station, active, Loc.GetString("wg-expedition-claimed",
            ("site", Loc.GetString(site.Name)), ("world", Name(offer.Ground)), ("minutes", (int) mission.Duration.TotalMinutes),
            ("travel", (int) WgTravelTime.TotalMinutes)));
        UpdateConsoles(station, data);
        return true;
    }

    /// <summary>The console's finish button on a site mission gives it up: the site is released and the failed cooldown runs.</summary>
    private bool WgTryFinishPlanetMission(EntityUid station, SalvageExpeditionDataComponent data)
    {
        if (!TryComp<WGPlanetExpeditionComponent>(station, out var comp) || comp.Active == null)
            return false;

        WgEnd(station, data, comp, false, Loc.GetString("wg-expedition-abandoned"));
        return true;
    }

    /// <summary>Spawns the mission's objectives and hostiles on the site's free floor; returns what must be cleared.</summary>
    private List<EntityUid> WgArmSite(EntityUid ground, WGPlanetPoiSite site, SalvageMission mission, SalvageMissionParams missionParams, List<EntityUid> hostiles)
    {
        var objectives = new List<EntityUid>();
        if (!TryComp<MapGridComponent>(ground, out var grid))
            return objectives;

        var random = new Random(missionParams.Seed);
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var tiles = WgSiteTiles(ground, grid, site, random);
        var cursor = 0;

        EntityUid? Place(string proto)
        {
            while (cursor < tiles.Count)
            {
                var tile = tiles[cursor++];
                if (!_anchorable.TileFree(grid, tile, (int) CollisionGroup.MachineLayer, (int) CollisionGroup.MachineMask))
                    continue;

                var uid = EntityManager.CreateEntityUninitialized(proto, _mapSystem.GridTileToLocal(ground, grid, tile));
                RemComp<GhostTakeoverAvailableComponent>(uid);
                RemComp<GhostRoleComponent>(uid);
                EntityManager.InitializeAndStartEntity(uid);
                return uid;
            }

            return null;
        }

        var scale = 1f;
        switch (missionParams.MissionType)
        {
            case SalvageMissionType.Destruction:
                for (var i = 0; i < GetStructureCount(missionParams.Difficulty); i++)
                {
                    if (Place(faction.Configs["DefenseStructure"]) is not { } structure)
                        break;

                    AddComp<SalvageStructureComponent>(structure);
                    objectives.Add(structure);
                }

                break;
            case SalvageMissionType.Elimination:
                if (Place(faction.Configs["Megafauna"]) is { } megafauna)
                    objectives.Add(megafauna);

                // Fewer groups, as upstream: the creature is the fight.
                scale = 0.5f;
                break;
        }

        var groups = GetSpawnCount(missionParams.Difficulty) * scale;
        var groupSum = faction.MobGroups.Sum(group => group.Prob);

        for (var i = 0; i < groups; i++)
        {
            var roll = random.NextFloat() * groupSum;
            var value = 0f;

            foreach (var group in faction.MobGroups)
            {
                value += group.Prob;
                if (value < roll)
                    continue;

                foreach (var proto in EntitySpawnCollection.GetSpawns(group.Entries, random))
                {
                    if (proto != null && Place(proto) is { } hostile)
                        hostiles.Add(hostile);
                }

                break;
            }
        }

        return objectives;
    }

    /// <summary>The site's covered floor first, shuffled, then its open floor, so objectives sit inside its buildings.</summary>
    private List<Vector2i> WgSiteTiles(EntityUid ground, MapGridComponent grid, WGPlanetPoiSite site, Random random)
    {
        var covered = new List<Vector2i>();
        var open = new List<Vector2i>();
        TryComp<RoofComponent>(ground, out var roof);

        for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
        for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
        {
            var tile = new Vector2i(x, y);
            if (_mapSystem.GetTileRef(ground, grid, tile).Tile.IsEmpty)
                continue;

            if (roof != null && _wgRoof.IsRooved((ground, grid, roof), tile))
                covered.Add(tile);
            else
                open.Add(tile);
        }

        random.Shuffle(covered);
        random.Shuffle(open);
        covered.AddRange(open);
        return covered;
    }

    /// <summary>Tracks every station's site mission: objectives cleared, time running out, the world gone.</summary>
    private void WgUpdatePlanetExpeditions()
    {
        if (_timing.CurTime < _wgNextCheck)
            return;

        _wgNextCheck = _timing.CurTime + WgCheckInterval;

        var query = EntityQueryEnumerator<WGPlanetExpeditionComponent, SalvageExpeditionDataComponent>();
        while (query.MoveNext(out var station, out var comp, out var data))
        {
            if (comp.Active is not { } active)
                continue;

            if (TerminatingOrDeleted(active.Ground))
            {
                WgEnd(station, data, comp, false, Loc.GetString("wg-expedition-failed"));
                continue;
            }

            var before = active.Objectives.Count;
            active.Objectives.RemoveAll(uid => Deleted(uid) || _mobState.IsDead(uid));

            if (active.Objectives.Count == 0)
            {
                WgEnd(station, data, comp, true, Loc.GetString("wg-expedition-completed"));
                continue;
            }

            if (active.Objectives.Count != before)
            {
                var key = active.Params.MissionType == SalvageMissionType.Destruction
                    ? "salvage-expedition-structure-remaining"
                    : "salvage-expedition-megafauna-remaining";
                WgAnnounce(station, active, Loc.GetString(key, ("count", active.Objectives.Count)));
            }

            // The clock only runs once the crew is there; until then it is the time to get there that counts.
            if (!active.Arrived)
            {
                if (WgArrived(station, active))
                {
                    active.Arrived = true;
                    active.EndTime = _timing.CurTime + active.Mission.Duration;
                    data.NextOffer = active.EndTime + TimeSpan.FromSeconds(1);
                    WgAnnounce(station, active, Loc.GetString("wg-expedition-arrived", ("minutes", (int) active.Mission.Duration.TotalMinutes)));
                }
                else if (_timing.CurTime > active.ArriveBy)
                {
                    WgEnd(station, data, comp, false, Loc.GetString("wg-expedition-never-arrived"));
                }
                else if (!active.WarnedTravel && active.ArriveBy - _timing.CurTime < TimeSpan.FromMinutes(5))
                {
                    active.WarnedTravel = true;
                    WgAnnounce(station, active, Loc.GetString("wg-expedition-travel-warning", ("minutes", 5)));
                }

                continue;
            }

            var remaining = active.EndTime - _timing.CurTime;
            if (remaining <= TimeSpan.Zero)
            {
                WgEnd(station, data, comp, false, Loc.GetString("wg-expedition-out-of-time"));
            }
            else if (!active.WarnedMinutes && remaining < TimeSpan.FromMinutes(5))
            {
                active.WarnedMinutes = true;
                WgAnnounce(station, active, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", 5)));
            }
            else if (!active.WarnedSeconds && remaining < TimeSpan.FromSeconds(45))
            {
                active.WarnedSeconds = true;
                WgAnnounce(station, active, Loc.GetString("salvage-expedition-announcement-countdown-seconds", ("duration", 45)));
            }
        }
    }

    /// <summary>
    /// Whether the crew has reached its site: the claiming ship landed at it or flying low over it, a driven vehicle at
    /// it, or anyone alive on foot at it.
    /// </summary>
    private bool WgArrived(EntityUid station, WGPlanetExpeditionActive active)
    {
        if (!TryComp<WGPlanetPoiComponent>(active.Ground, out var record) || active.SiteIndex >= record.Sites.Count)
            return false;

        var bounds = record.Sites[active.SiteIndex].Bounds;
        var area = new Box2(bounds.Left - WgArrivalMargin, bounds.Bottom - WgArrivalMargin,
            bounds.Right + WgArrivalMargin, bounds.Top + WgArrivalMargin);
        var network = CompOrNull<WGPlanetLayerComponent>(active.Ground)?.Network;

        bool At(EntityUid uid, bool anyLowLayer)
        {
            var xform = Transform(uid);
            if (xform.MapUid is not { } map)
                return false;

            var here = map == active.Ground
                || anyLowLayer && network != null && !HasComp<WGOrbitLayerComponent>(map)
                    && CompOrNull<WGPlanetLayerComponent>(map)?.Network == network;
            return here && area.Contains(_transform.GetWorldPosition(xform));
        }

        if (TryComp<StationDataComponent>(station, out var stationData))
        {
            foreach (var grid in stationData.Grids)
            {
                if (At(grid, true))
                    return true;
            }
        }

        var vehicles = EntityQueryEnumerator<RMCVehicleComponent>();
        while (vehicles.MoveNext(out var vehicle, out var comp))
        {
            if (comp.Operator != null && At(vehicle, false))
                return true;
        }

        foreach (var session in _wgPlayers.Sessions)
        {
            if (session.AttachedEntity is { } player && HasComp<MobStateComponent>(player) && !_mobState.IsDead(player) && At(player, false))
                return true;
        }

        return false;
    }

    /// <summary>Ends the station's site mission: the site is cleared or released, the rewards paid or the failed cooldown run.</summary>
    private void WgEnd(EntityUid station, SalvageExpeditionDataComponent data, WGPlanetExpeditionComponent comp, bool completed, string message)
    {
        var active = comp.Active!;
        comp.Active = null;
        WgAnnounce(station, active, message);
        WgRelease(active, completed);

        if (completed)
        {
            data.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_cooldown);
            WgGiveRewards(station, active.Mission.Rewards);
        }
        else
        {
            data.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_failedCooldown);
        }

        data.ActiveMission = 0;
        data.Cooldown = true;
        data.CanFinish = false;
        UpdateConsoles(station, data);
    }

    /// <summary>
    /// Frees a mission's site, cleared or not. What an unfinished mission left standing goes, objectives and living
    /// hostiles both, so the next claim of the site doesn't stack on it.
    /// </summary>
    private void WgRelease(WGPlanetExpeditionActive active, bool completed)
    {
        if (TryComp<WGPlanetPoiComponent>(active.Ground, out var record) && active.SiteIndex < record.Sites.Count)
        {
            var site = record.Sites[active.SiteIndex];
            site.ExpeditionClaim = null;
            site.Cleared = completed;
            site.ClearedAt = _timing.CurTime;
            // Survivors of a cleared site go when it recovers, so the next claim doesn't stack on them.
            if (completed)
                site.Leftovers = new List<EntityUid>(active.Hostiles);
        }

        if (completed)
            return;

        foreach (var objective in active.Objectives)
        {
            if (!TerminatingOrDeleted(objective))
                QueueDel(objective);
        }

        foreach (var hostile in active.Hostiles)
        {
            if (!TerminatingOrDeleted(hostile) && !_mobState.IsDead(hostile))
                QueueDel(hostile);
        }
    }

    /// <summary>A station deleted mid-mission, such as a sold ship, gives its site back.</summary>
    private void WgOnExpeditionShutdown(Entity<WGPlanetExpeditionComponent> station, ref ComponentShutdown args)
    {
        if (station.Comp.Active is not { } active)
            return;

        station.Comp.Active = null;
        WgRelease(active, false);
    }

    /// <summary>The mission's rewards, at one of the station's expedition consoles, as upstream pays them.</summary>
    private void WgGiveRewards(EntityUid station, List<string> rewards)
    {
        if (!_cfgManager.GetCVar(NFCCVars.SalvageExpeditionRewardsEnabled))
            return;

        var consoles = new List<EntityUid>();
        var query = EntityQueryEnumerator<SalvageExpeditionConsoleComponent>();
        while (query.MoveNext(out var console, out _))
        {
            if (_station.GetOwningStation(console) == station)
                consoles.Add(console);
        }

        if (consoles.Count == 0)
            return;

        foreach (var reward in rewards)
        {
            Spawn(reward, _transform.GetMapCoordinates(_random.Pick(consoles)));
        }
    }

    /// <summary>Radio to the station's hulls and to anyone on the ground near the site.</summary>
    private void WgAnnounce(EntityUid station, WGPlanetExpeditionActive active, string text)
    {
        var filter = Filter.Empty();
        var source = station;

        if (TryComp<StationDataComponent>(station, out var stationData))
        {
            foreach (var grid in stationData.Grids)
            {
                filter.AddInGrid(grid, EntityManager);
                source = grid;
            }
        }

        if (TryComp<WGPlanetPoiComponent>(active.Ground, out var record)
            && active.SiteIndex < record.Sites.Count
            && TryComp<MapComponent>(active.Ground, out var map))
        {
            var bounds = record.Sites[active.SiteIndex].Bounds;
            var middle = new Vector2((bounds.Left + bounds.Right) / 2f, (bounds.Bottom + bounds.Top) / 2f);
            filter.AddInRange(new MapCoordinates(middle, map.MapId), WgAnnounceRange);
        }

        _chat.ChatMessageToManyFiltered(filter, ChatChannel.Radio, text, text, source, false, true, null);
    }
}
