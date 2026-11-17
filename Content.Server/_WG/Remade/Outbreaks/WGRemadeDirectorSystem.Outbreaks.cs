using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Gifts;
using Content.Server._WG.Remade.Monolith;
using Content.Shared._WG.Remade;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.Remade.Director;

/// <summary>
/// Outbreaks. A stone at the Crescendo breaks out on other crewed ships across the sector: a few low Remade come out of the vents
/// near someone aboard, and a shard of the stone roots where they came out so the dead there turn too. It all goes with the stone.
/// </summary>
public sealed partial class WGRemadeDirectorSystem
{
    [Dependency] private WGCorruptionSystem _outbreakCorruption = default!;
    [Dependency] private WGShardSystem _shards = default!;

    private const float OutbreakFirstMin = 45f;
    private const float OutbreakFirstMax = 90f;
    private const float OutbreakEveryMin = 120f;
    private const float OutbreakEveryMax = 210f;
    private const float OutbreakRetry = 20f;
    private const int OutbreakSquadMin = 2;
    private const int OutbreakSquadMax = 3;

    private static readonly EntProtoId[] OutbreakMobs = { "WGRemadeHopper", "WGRemadeSlasher" };

    private void UpdateOutbreak(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        if (ent.Comp2.Stage < WGMonolithStage.Crescendo || ent.Comp2.Local)
        {
            director.NextOutbreak = TimeSpan.Zero;
            return;
        }

        if (director.NextOutbreak == TimeSpan.Zero)
        {
            director.NextOutbreak = now + TimeSpan.FromSeconds(_random.NextFloat(OutbreakFirstMin, OutbreakFirstMax));
            return;
        }

        if (now < director.NextOutbreak)
            return;

        director.NextOutbreak = now + TimeSpan.FromSeconds(TryOutbreak(ent, now)
            ? _random.NextFloat(OutbreakEveryMin, OutbreakEveryMax)
            : OutbreakRetry);
    }

    /// <summary>Breaks out on one crewed ship other than the stone's own. False if there is none it can reach.</summary>
    public bool TryOutbreak(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, TimeSpan now)
    {
        if (CountSector() >= SectorCap)
            return false;

        var xform = Transform(ent);
        var candidates = new List<EntityUid>();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out _, out var player))
        {
            if (player.GridUid is not { } grid || grid == xform.GridUid || player.MapID != xform.MapID
                || HasComp<WGRemadeComponent>(uid) || _mobState.IsDead(uid) || _outbreakCorruption.IsSanctuaryAt(uid))
            {
                continue;
            }

            candidates.Add(uid);
        }

        _random.Shuffle(candidates);
        foreach (var prey in candidates)
        {
            if (BreakOut(ent, prey, now))
                return true;
        }

        return false;
    }

    /// <summary>Sends a few low Remade out of the vents near one crewman and roots a shard where the first came out.</summary>
    private bool BreakOut(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, EntityUid prey, TimeSpan now)
    {
        if (Transform(prey).GridUid is not { } grid)
            return false;

        // The arrival points hide from the crew aboard that ship, not the crew round the stone.
        _crew.Clear();
        var aboard = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (aboard.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid == grid && !HasComp<WGRemadeComponent>(uid) && !_mobState.IsDead(uid))
                _crew.Add(uid);
        }

        var points = ArrivalPoints(ent, prey, grid);
        if (points.Count == 0)
            return false;

        var squad = _random.Next(OutbreakSquadMin, OutbreakSquadMax + 1);
        for (var i = 0; i < squad; i++)
        {
            Deliver(ent, prey, _random.Pick(OutbreakMobs), points[i % points.Count], WGRemadeMode.Hunt, null, now);
        }

        if (!HasRootedShardOn(ent.Comp2, grid))
            _shards.PlantRooted((ent.Owner, ent.Comp2), points[0].At);

        ent.Comp1.Outbreaks++;
        return true;
    }

    private bool HasRootedShardOn(WGMonolithComponent monolith, EntityUid grid)
    {
        foreach (var shard in monolith.Shards)
        {
            if (TryComp<WGMonolithShardComponent>(shard, out var comp) && comp.Deployed && Transform(shard).GridUid == grid)
                return true;
        }

        return false;
    }
}
