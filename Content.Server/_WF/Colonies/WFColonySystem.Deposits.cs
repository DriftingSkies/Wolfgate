using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.Colonies;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.Colonies;

/// <summary>Deposits: each world gets the resource nodes its colony prototype lists, placed once a round once its ground has bounds.</summary>
public sealed partial class WFColonySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;

    /// <summary>The share of a world's radius deposits are placed within, away from its edge.</summary>
    private const float DepositSpread = 0.6f;

    /// <summary>Grounds that got their deposits this round.</summary>
    private readonly HashSet<EntityUid> _seeded = new();

    /// <summary>Whether a place on a ground lies within camp range of an outpost of a Colony-tier world.</summary>
    public bool NearColony(EntityUid ground, Vector2i tile)
    {
        if (!TryWorldOf(ground, out var world, out _) || Tier(world) < WFWorldTier.Colony)
            return false;

        var range = _cfg.GetCVar(ColonyCVars.CampRange);
        var query = EntityQueryEnumerator<Outposts.Components.WFOutpostComponent>();
        while (query.MoveNext(out var outpost))
        {
            if (outpost.Ground == ground && !outpost.Abandoned && !TerminatingOrDeleted(outpost.Console)
                && (_transform.GetWorldPosition(outpost.Console) - tile).Length() <= range)
                return true;
        }

        return false;
    }

    /// <summary>Places a world's deposits on its ground the first time its bounds are known.</summary>
    private void SeedDeposits(EntityUid network)
    {
        var comp = Comp<WFPlanetNetworkComponent>(network);
        var ground = comp.GroundMap;
        if (_seeded.Contains(ground) || TerminatingOrDeleted(ground) || !TryComp<MapComponent>(ground, out var map))
            return;

        var centre = comp.Centre;
        var radius = 150f;
        if (_bounds.TryGetBounds(ground, out var boundsCentre, out var boundsRadius))
        {
            centre = boundsCentre;
            radius = boundsRadius * DepositSpread;
        }

        _seeded.Add(ground);
        foreach (var world in _proto.EnumeratePrototypes<WFColonyWorldPrototype>())
        {
            if (world.Surface != comp.Surface)
                continue;

            foreach (var entry in world.Deposits)
            {
                for (var i = 0; i < entry.Count; i++)
                {
                    var offset = _random.NextAngle().RotateVec(new Vector2(radius * MathF.Sqrt(_random.NextFloat()), 0f));
                    Spawn(entry.Deposit, new MapCoordinates(centre + offset, map.MapId));
                }
            }
        }
    }
}
