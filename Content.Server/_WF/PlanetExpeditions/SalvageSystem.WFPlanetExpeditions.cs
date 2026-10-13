using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetExpeditions;
using Content.Server._WF.PlanetPois;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions.Structure;
using Content.Shared._NF.CCVar;
using Content.Shared._WF.CCVar;
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
    [Dependency] private WFPlanetPoiSystem _wfPois = default!;
    [Dependency] private SharedRoofSystem _wfRoof = default!;

    private static readonly TimeSpan WfCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>How far from the site's middle a crew on the ground hears the mission's announcements.</summary>
    private const float WfAnnounceRange = 96f;

    private TimeSpan _wfNextCheck;

    private void WfInitializePlanetExpeditions()
    {
        SubscribeLocalEvent<WFPlanetExpeditionComponent, ComponentShutdown>(WfOnExpeditionShutdown);
    }

    /// <summary>
    /// Fills the station's offers from free sites on the worlds; false when there are none, so upstream generates
    /// its maps instead.
    /// </summary>
    private bool WfTryOfferPlanetMissions(EntityUid station, SalvageExpeditionDataComponent data)
    {
        if (!_cfgManager.GetCVar(PlanetCVars.PlanetExpeditions))
            return false;

        var free = new List<(EntityUid Ground, int Index)>();
        var query = EntityQueryEnumerator<WFPlanetPoiComponent>();
        while (query.MoveNext(out var ground, out var record))
        {
            // A world still placing its sites has nothing to stand an objective on yet.
            if (!record.NetworkReady)
                continue;

            for (var i = 0; i < record.Sites.Count; i++)
            {
                var site = record.Sites[i];
                if (site.Expeditions && site.ExpeditionClaim == null && !site.Cleared)
                    free.Add((ground, i));
            }
        }

        if (free.Count == 0)
            return false;

        var offers = EnsureComp<WFPlanetExpeditionComponent>(station);
        offers.Offers.Clear();
        data.Missions.Clear();
        _random.Shuffle(free);

        foreach (var (ground, index) in free.Take(MissionLimit))
        {
            var site = Comp<WFPlanetPoiComponent>(ground).Sites[index];
            var mission = new SalvageMissionParams
            {
                Index = data.NextIndex,
                MissionType = _random.Prob(0.5f) ? SalvageMissionType.Destruction : SalvageMissionType.Elimination,
                Seed = _random.Next(),
                Difficulty = site.Difficulty,
                WfSite = site.Name,
                WfWorld = Name(ground),
            };

            data.Missions[data.NextIndex] = mission;
            offers.Offers[data.NextIndex] = new WFPlanetExpeditionOffer(ground, index);
            data.NextIndex++;
        }

        return true;
    }

    /// <summary>
    /// Claims a site mission in place: no jump, the site is armed and its signal named. False when the mission is
    /// not a site's, so upstream handles it.
    /// </summary>
    private bool WfTryClaimPlanetMission(EntityUid console, SalvageExpeditionConsoleComponent component, EntityUid station, SalvageExpeditionDataComponent data, SalvageMissionParams missionParams)
    {
        if (!TryComp<WFPlanetExpeditionComponent>(station, out var comp) || !comp.Offers.TryGetValue(missionParams.Index, out var offer))
            return false;

        if (!TryComp<WFPlanetPoiComponent>(offer.Ground, out var record)
            || offer.SiteIndex >= record.Sites.Count
            || record.Sites[offer.SiteIndex] is not { ExpeditionClaim: null, Cleared: false } site)
        {
            // Another crew got there first; fresh offers next tick.
            PlayDenySound(console, component);
            _popupSystem.PopupEntity(Loc.GetString("wf-expedition-site-taken"), console, PopupType.MediumCaution);
            data.NextOffer = _timing.CurTime;
            UpdateConsoles(station, data);
            return true;
        }

        var mission = GetMission(missionParams.MissionType, missionParams.Difficulty, missionParams.Seed);
        var hostiles = new List<EntityUid>();
        var objectives = WfArmSite(offer.Ground, site, mission, missionParams, hostiles);

        if (objectives.Count == 0)
        {
            // Nothing to clear would complete, and pay, at once. The site has no room; it is not offered again.
            foreach (var hostile in hostiles)
                QueueDel(hostile);

            site.Expeditions = false;
            Log.Warning($"{site.Name} on {ToPrettyString(offer.Ground)} had no room for an expedition's objectives.");
            PlayDenySound(console, component);
            _popupSystem.PopupEntity(Loc.GetString("wf-expedition-site-unusable"), console, PopupType.MediumCaution);
            data.NextOffer = _timing.CurTime;
            UpdateConsoles(station, data);
            return true;
        }

        var active = new WFPlanetExpeditionActive
        {
            Ground = offer.Ground,
            SiteIndex = offer.SiteIndex,
            Params = missionParams,
            Mission = mission,
            Objectives = objectives,
            Hostiles = hostiles,
            EndTime = _timing.CurTime + mission.Duration,
        };

        site.ExpeditionClaim = station;
        comp.Active = active;
        data.ActiveMission = missionParams.Index;
        data.NextOffer = _timing.CurTime + mission.Duration + TimeSpan.FromSeconds(1);
        data.CanFinish = true;

        if (TryComp<WFPoiSignalComponent>(site.Signal, out var signal) && !signal.Revealed)
            _wfPois.Reveal((site.Signal, signal), null);

        WfAnnounce(station, active, Loc.GetString("wf-expedition-claimed",
            ("site", Loc.GetString(site.Name)), ("world", Name(offer.Ground)), ("minutes", (int) mission.Duration.TotalMinutes)));
        UpdateConsoles(station, data);
        return true;
    }

    /// <summary>The console's finish button on a site mission gives it up: the site is released and the failed cooldown runs.</summary>
    private bool WfTryFinishPlanetMission(EntityUid station, SalvageExpeditionDataComponent data)
    {
        if (!TryComp<WFPlanetExpeditionComponent>(station, out var comp) || comp.Active == null)
            return false;

        WfEnd(station, data, comp, false, Loc.GetString("wf-expedition-abandoned"));
        return true;
    }

    /// <summary>Spawns the mission's objectives and hostiles on the site's free floor; returns what must be cleared.</summary>
    private List<EntityUid> WfArmSite(EntityUid ground, WFPlanetPoiSite site, SalvageMission mission, SalvageMissionParams missionParams, List<EntityUid> hostiles)
    {
        var objectives = new List<EntityUid>();
        if (!TryComp<MapGridComponent>(ground, out var grid))
            return objectives;

        var random = new Random(missionParams.Seed);
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var tiles = WfSiteTiles(ground, grid, site, random);
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
    private List<Vector2i> WfSiteTiles(EntityUid ground, MapGridComponent grid, WFPlanetPoiSite site, Random random)
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

            if (roof != null && _wfRoof.IsRooved((ground, grid, roof), tile))
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
    private void WfUpdatePlanetExpeditions()
    {
        if (_timing.CurTime < _wfNextCheck)
            return;

        _wfNextCheck = _timing.CurTime + WfCheckInterval;

        var query = EntityQueryEnumerator<WFPlanetExpeditionComponent, SalvageExpeditionDataComponent>();
        while (query.MoveNext(out var station, out var comp, out var data))
        {
            if (comp.Active is not { } active)
                continue;

            if (TerminatingOrDeleted(active.Ground))
            {
                WfEnd(station, data, comp, false, Loc.GetString("wf-expedition-failed"));
                continue;
            }

            var before = active.Objectives.Count;
            active.Objectives.RemoveAll(uid => Deleted(uid) || _mobState.IsDead(uid));

            if (active.Objectives.Count == 0)
            {
                WfEnd(station, data, comp, true, Loc.GetString("wf-expedition-completed"));
                continue;
            }

            if (active.Objectives.Count != before)
            {
                var key = active.Params.MissionType == SalvageMissionType.Destruction
                    ? "salvage-expedition-structure-remaining"
                    : "salvage-expedition-megafauna-remaining";
                WfAnnounce(station, active, Loc.GetString(key, ("count", active.Objectives.Count)));
            }

            var remaining = active.EndTime - _timing.CurTime;
            if (remaining <= TimeSpan.Zero)
            {
                WfEnd(station, data, comp, false, Loc.GetString("wf-expedition-out-of-time"));
            }
            else if (!active.WarnedMinutes && remaining < TimeSpan.FromMinutes(5))
            {
                active.WarnedMinutes = true;
                WfAnnounce(station, active, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", 5)));
            }
            else if (!active.WarnedSeconds && remaining < TimeSpan.FromSeconds(45))
            {
                active.WarnedSeconds = true;
                WfAnnounce(station, active, Loc.GetString("salvage-expedition-announcement-countdown-seconds", ("duration", 45)));
            }
        }
    }

    /// <summary>Ends the station's site mission: the site is cleared or released, the rewards paid or the failed cooldown run.</summary>
    private void WfEnd(EntityUid station, SalvageExpeditionDataComponent data, WFPlanetExpeditionComponent comp, bool completed, string message)
    {
        var active = comp.Active!;
        comp.Active = null;
        WfAnnounce(station, active, message);
        WfRelease(active, completed);

        if (completed)
        {
            data.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_cooldown);
            WfGiveRewards(station, active.Mission.Rewards);
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
    private void WfRelease(WFPlanetExpeditionActive active, bool completed)
    {
        if (TryComp<WFPlanetPoiComponent>(active.Ground, out var record) && active.SiteIndex < record.Sites.Count)
        {
            var site = record.Sites[active.SiteIndex];
            site.ExpeditionClaim = null;
            site.Cleared = completed;
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
    private void WfOnExpeditionShutdown(Entity<WFPlanetExpeditionComponent> station, ref ComponentShutdown args)
    {
        if (station.Comp.Active is not { } active)
            return;

        station.Comp.Active = null;
        WfRelease(active, false);
    }

    /// <summary>The mission's rewards, at one of the station's expedition consoles, as upstream pays them.</summary>
    private void WfGiveRewards(EntityUid station, List<string> rewards)
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
    private void WfAnnounce(EntityUid station, WFPlanetExpeditionActive active, string text)
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

        if (TryComp<WFPlanetPoiComponent>(active.Ground, out var record)
            && active.SiteIndex < record.Sites.Count
            && TryComp<MapComponent>(active.Ground, out var map))
        {
            var bounds = record.Sites[active.SiteIndex].Bounds;
            var middle = new Vector2((bounds.Left + bounds.Right) / 2f, (bounds.Bottom + bounds.Top) / 2f);
            filter.AddInRange(new MapCoordinates(middle, map.MapId), WfAnnounceRange);
        }

        _chat.ChatMessageToManyFiltered(filter, ChatChannel.Radio, text, text, source, false, true, null);
    }
}
