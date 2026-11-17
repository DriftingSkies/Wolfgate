using System.Linq;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Monolith;
using Content.Server._WG.Remade.Structures;
using Content.Shared._WG.Remade;
using Content.Shared._WG.Remade.Structures;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.Remade.Director;

/// <summary>
/// Building. Between waves the Director spends biomass on growths, always out of the crew's sight: nodes first, to push the corruption
/// towards where the crew spend their time, then maws, snares and cysts in corridors near them, and harvesters and nests further back.
/// An Infector does the building where one is alive, walking to the site and working there for a few seconds; with none, the growth
/// is put down as a seed and swells up slowly. A reserve is held back so waves still get paid for.
/// </summary>
public sealed partial class WGRemadeDirectorSystem
{
    [Dependency] private WGGrowthSystem _growth = default!;

    private static readonly EntProtoId InfectorProto = "WGRemadeInfector";
    private static readonly TimeSpan JobRate = TimeSpan.FromSeconds(0.5);
    private static readonly SoundSpecifier WorkSound = new SoundCollectionSpecifier("WGRemadeInfectorWork");
    private const float JobGiveUp = 120f;
    private const float WorkReach = 1.8f;
    private const float NodeGap = 6f;
    private const int SiteSamples = 30;

    private TimeSpan _nextJob;

    /// <summary>The most growths one Monolith keeps at a stage.</summary>
    public static int GrowthCap(WGMonolithStage stage)
    {
        return stage switch
        {
            WGMonolithStage.Growth => 6,
            WGMonolithStage.Harvest => 12,
            WGMonolithStage.Rapture => 20,
            WGMonolithStage.Crescendo => 28,
            _ => 0,
        };
    }

    private void Build(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;
        if (now < director.NextBuild || stage < WGMonolithStage.Growth)
            return;

        director.NextBuild = now + TimeSpan.FromSeconds(_random.NextFloat(director.BuildEveryMin, director.BuildEveryMax));
        director.Growths.RemoveWhere(uid => TerminatingOrDeleted(uid));
        if (director.Phase is WGRemadeDirectorPhase.Stalk or WGRemadeDirectorPhase.Peak
            || _crew.Count == 0
            || director.Growths.Count >= GrowthCap(stage))
        {
            return;
        }

        var target = _random.Pick(_crew);
        foreach (var def in BuildOrder(ent, Reserve(director, stage)))
        {
            if (FindSite(ent, def, target) is not { } site)
                continue;

            BuildAt(ent, def, site, now);
            return;
        }
    }

    /// <summary>Biomass kept back from building: enough for a couple of the cheapest Remade it could send.</summary>
    private float Reserve(WGRemadeDirectorComponent director, WGMonolithStage stage)
    {
        var cheapest = float.MaxValue;
        foreach (var mob in _proto.EnumeratePrototypes<WGRemadeMobPrototype>())
        {
            if (mob.SpawnWeight > 0f && !mob.NeedsCorpses && stage >= mob.MinStage)
                cheapest = Math.Min(cheapest, mob.Cost);
        }

        return cheapest == float.MaxValue ? director.BuildReserve : Math.Max(director.BuildReserve, cheapest * 2f);
    }

    /// <summary>What it could build now, in the order it should try: nodes first, then the rest, each by weight.</summary>
    private List<WGRemadeGrowthPrototype> BuildOrder(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, float reserve)
    {
        var director = ent.Comp1;
        var nodes = new List<WGRemadeGrowthPrototype>();
        var rest = new List<WGRemadeGrowthPrototype>();
        foreach (var def in _growth.KindsAt(ent.Comp2.Stage))
        {
            if (director.Biomass - def.Cost < reserve
                || CountGrowths(director, def) >= WGGrowthSystem.MaxAlive(def, ent.Comp2.Stage))
            {
                continue;
            }

            (def.Site == WGRemadeSiteKind.Frontier ? nodes : rest).Add(def);
        }

        var order = WeightedOrder(nodes);
        order.AddRange(WeightedOrder(rest));
        return order;
    }

    private List<WGRemadeGrowthPrototype> WeightedOrder(List<WGRemadeGrowthPrototype> defs)
    {
        var order = new List<WGRemadeGrowthPrototype>();
        var pool = new List<WGRemadeGrowthPrototype>(defs);
        while (pool.Count > 0)
        {
            var roll = _random.NextFloat(0f, pool.Sum(def => def.Weight));
            var pick = pool[^1];
            foreach (var def in pool)
            {
                roll -= def.Weight;
                if (roll <= 0f)
                {
                    pick = def;
                    break;
                }
            }

            order.Add(pick);
            pool.Remove(pick);
        }

        return order;
    }

    private int CountGrowths(WGRemadeDirectorComponent director, WGRemadeGrowthPrototype def)
    {
        var count = 0;
        foreach (var uid in director.Growths)
        {
            if (TryComp<WGGrowthComponent>(uid, out var growth) && growth.Kind == def.ID)
                count++;
        }

        return count;
    }

    /// <summary>Picks where a growth of this kind should go, for a crewman it is meant to trouble. Null if nowhere suits.</summary>
    private EntityCoordinates? FindSite(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, WGRemadeGrowthPrototype def, EntityUid target)
    {
        var grid = Transform(target).GridUid;
        if (grid == null)
            return null;

        var here = WorldPos(target);
        var tiles = new List<(EntityCoordinates At, float Distance)>();
        foreach (var uid in ent.Comp2.Corruption)
        {
            if (!TryComp(uid, out TransformComponent? xform)
                || xform.GridUid != grid
                || TryComp<WGCorruptionComponent>(uid, out var tile) && tile.DecayAt != null)
            {
                continue;
            }

            tiles.Add((xform.Coordinates, (_transform.GetWorldPosition(xform) - here).Length()));
        }

        if (tiles.Count == 0)
            return null;

        if (def.Site == WGRemadeSiteKind.Frontier)
        {
            // The corruption's edge nearest them, unless it has reached them already.
            tiles.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (tiles[0].Distance < NodeGap)
                return null;

            foreach (var (at, _) in tiles.Take(SiteSamples))
            {
                if (_growth.CanPlace(def, at) && Unseen(def, at))
                    return at;
            }

            return null;
        }

        var (min, max) = def.Site switch
        {
            WGRemadeSiteKind.Choke => (3f, 22f),
            WGRemadeSiteKind.Rear => (16f, float.MaxValue),
            _ => (3f, 14f),
        };

        var candidates = tiles.Where(t => t.Distance >= min && t.Distance <= max).ToList();
        _random.Shuffle(candidates);

        EntityCoordinates? best = null;
        var bestScore = -1f;
        foreach (var (at, distance) in candidates.Take(SiteSamples))
        {
            if (!_growth.CanPlace(def, at) || !Unseen(def, at))
                continue;

            float score;
            switch (def.Site)
            {
                case WGRemadeSiteKind.Choke:
                    score = _growth.Confinement(at);
                    if (score < (def.RequireWall ? 1f : 2f))
                        continue;

                    break;
                case WGRemadeSiteKind.Rear:
                    score = distance;
                    break;
                default:
                    return at;
            }

            if (score > bestScore)
            {
                best = at;
                bestScore = score;
            }
        }

        return best;
    }

    private bool Unseen(WGRemadeGrowthPrototype def, EntityCoordinates at)
    {
        return def.InSight || !SeenByCrew(at, CrewSight);
    }

    private void BuildAt(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, WGRemadeGrowthPrototype def, EntityCoordinates site, TimeSpan now)
    {
        if (FindBuilder(ent.Comp1, site) is { } builder)
        {
            StartJob(ent, builder, def, site, now);
            return;
        }

        // No Infector to raise it: it goes down as a seed and swells up on its own.
        if (_growth.TryBuild(def, site, ent.Owner, ready: false) != null)
            ent.Comp1.Built++;
    }

    private bool IsInfector(EntityUid uid)
    {
        return MetaData(uid).EntityPrototype?.ID == InfectorProto.Id;
    }

    /// <summary>The nearest free Infector on the site's grid, out in the world and not already at a job.</summary>
    private EntityUid? FindBuilder(WGRemadeDirectorComponent director, EntityCoordinates site)
    {
        var grid = _transform.GetGrid(site);
        var at = _transform.ToMapCoordinates(site).Position;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var uid in director.Horde)
        {
            if (!IsInfector(uid)
                || HasComp<WGRemadeBuildJobComponent>(uid)
                || HasComp<WGRemadePlayingDeadComponent>(uid)
                || HasComp<WGRemadeConvertingComponent>(uid)
                || _mobState.IsIncapacitated(uid)
                || Transform(uid).GridUid != grid
                || !TryComp<WGRemadeBehaviourComponent>(uid, out var behaviour)
                || behaviour.Mode != WGRemadeMode.Hunt
                || InFight(uid))
            {
                continue;
            }

            var distance = (WorldPos(uid) - at).LengthSquared();
            if (distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void StartJob(
        Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent,
        EntityUid builder,
        WGRemadeGrowthPrototype def,
        EntityCoordinates site,
        TimeSpan now)
    {
        var job = EnsureComp<WGRemadeBuildJobComponent>(builder);
        job.Kind = def.ID;
        job.Site = site;
        job.Monolith = ent.Owner;
        job.GiveUpAt = now + TimeSpan.FromSeconds(JobGiveUp);
        job.WorkUntil = null;

        var behaviour = Comp<WGRemadeBehaviourComponent>(builder);
        behaviour.Mode = WGRemadeMode.Build;
        SetKnown(builder, behaviour, site, now);
    }

    private void UpdateJobs(TimeSpan now)
    {
        if (now < _nextJob)
            return;

        _nextJob = now + JobRate;
        var query = EntityQueryEnumerator<WGRemadeBuildJobComponent>();
        while (query.MoveNext(out var uid, out var job))
        {
            if (TerminatingOrDeleted(job.Monolith) || _mobState.IsIncapacitated(uid) || now >= job.GiveUpAt)
            {
                EndJob(uid, job);
                continue;
            }

            if (job.WorkUntil is not { } until)
            {
                if (Arrived(uid, job.Site, WorkReach))
                {
                    job.WorkUntil = now + TimeSpan.FromSeconds(_proto.Index(job.Kind).WorkSeconds);
                    Freeze(uid);
                }
                else if (TryComp<WGRemadeBehaviourComponent>(uid, out var behaviour) && behaviour.Known == null)
                {
                    SetKnown(uid, behaviour, job.Site, now);
                }

                continue;
            }

            if (now >= job.NextSound)
            {
                job.NextSound = now + TimeSpan.FromSeconds(1.4);
                _audio.PlayPvs(WorkSound, uid);
            }

            if (now < until)
                continue;

            if (_growth.TryBuild(_proto.Index(job.Kind), job.Site, job.Monolith, ready: true) != null
                && TryComp<WGRemadeDirectorComponent>(job.Monolith, out var director))
            {
                director.Built++;
            }

            EndJob(uid, job);
        }
    }

    private void EndJob(EntityUid builder, WGRemadeBuildJobComponent job)
    {
        RemCompDeferred<WGRemadeBuildJobComponent>(builder);

        // The hunt target goes before the brain wakes, or a plan made with it can start after it is gone.
        if (TryComp<WGRemadeBehaviourComponent>(builder, out var behaviour))
        {
            behaviour.Mode = WGRemadeMode.Hunt;
            ClearKnown(builder, behaviour);
        }

        if (job.WorkUntil != null && !TerminatingOrDeleted(builder))
            Thaw(builder);
    }

    /// <summary>The kind of Remade a nest should make: any the Director could send at this stage that needs no corpse, by weight.</summary>
    public EntProtoId? PickNestMob(EntityUid monolith)
    {
        if (!TryComp<WGMonolithComponent>(monolith, out var comp))
            return null;

        var options = _proto.EnumeratePrototypes<WGRemadeMobPrototype>()
            .Where(mob => mob.SpawnWeight > 0f && !mob.NeedsCorpses && comp.Stage >= mob.MinStage)
            .ToList();

        if (options.Count == 0)
            return null;

        return PickWeighted(options).Entity;
    }

    /// <summary>
    /// Makes one Remade next to a place for a Monolith, paid from its biomass. Null when the caps are met or it can't afford it.
    /// </summary>
    public EntityUid? TryProduce(EntityUid monolith, EntProtoId proto, EntityCoordinates near)
    {
        if (!TryComp<WGRemadeDirectorComponent>(monolith, out var director)
            || !TryComp<WGMonolithComponent>(monolith, out var comp)
            || director.Horde.Count >= HordeCap(comp.Stage)
            || CountSector() >= SectorCap)
        {
            return null;
        }

        var mob = _proto.EnumeratePrototypes<WGRemadeMobPrototype>().FirstOrDefault(mob => mob.Entity == proto);
        var cost = mob?.Cost ?? 50f;
        if (director.Biomass < cost)
            return null;

        director.Biomass -= cost;
        var remade = Spawn(proto, OpenSpotNear(near, 1.6f));
        Enlist(remade, monolith);
        return remade;
    }
}
