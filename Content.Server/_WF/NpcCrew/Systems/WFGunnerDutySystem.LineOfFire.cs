using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Clean shots: a gun holds its fire while a ship that is not hostile to its crew lies between it and its target, a
/// bystander or a friend, rather than put rounds through it. Companies with reckless fire in their standing shoot
/// through whatever is in the way.
/// </summary>
public sealed partial class WFGunnerDutySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>How long a gun's clear or fouled line is trusted before it is looked at again.</summary>
    private static readonly TimeSpan LineInterval = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<(EntityUid Gun, EntityUid Target), (TimeSpan Next, bool Clear)> _lines = new();

    /// <summary>The guns of a list that have a clean line to the aim point on the target.</summary>
    public List<EntityUid> ClearGuns(EntityUid grid, string group, IEnumerable<EntityUid> guns, EntityUid target, MapCoordinates at)
    {
        if (Reckless(grid))
            return guns.ToList();

        var now = _timing.CurTime;
        var clear = new List<EntityUid>();
        foreach (var gun in guns)
        {
            var key = (gun, target);
            if (!_lines.TryGetValue(key, out var line) || now >= line.Next)
            {
                line = (now + LineInterval, ClearLine(grid, group, gun, target, at));
                _lines[key] = line;
            }

            if (line.Clear)
                clear.Add(gun);
        }

        if (_lines.Count > 512)
            _lines.Clear();

        return clear;
    }

    /// <summary>Whether a ship's company fires through whatever is in its way.</summary>
    private bool Reckless(EntityUid grid)
    {
        return CompOrNull<CompanyComponent>(grid)?.CompanyName.Id is { Length: > 0 } company
               && _prototypes.TryIndex<WFStandingPrototype>(company, out var standing) && standing.RecklessFire;
    }

    /// <summary>
    /// Whether nothing between a gun and its aim point is a ship it must spare: anything but its own hull, the target
    /// and ships its crew already counts as hostile.
    /// </summary>
    private bool ClearLine(EntityUid grid, string group, EntityUid gun, EntityUid target, MapCoordinates at)
    {
        var from = _transform.GetMapCoordinates(gun);
        if (from.MapId != at.MapId)
            return false;

        var offset = at.Position - from.Position;
        var length = offset.Length();
        if (length < 1f)
            return true;

        var mapUid = Transform(grid).MapUid;
        var ray = new CollisionRay(from.Position, offset / length, (int) (CollisionGroup.Opaque | CollisionGroup.Impassable));
        return !_physics.IntersectRayWithPredicate(from.MapId, ray, (grid, group, target, mapUid),
            (hit, state) => Transform(hit).GridUid is not { } other || other == state.grid || other == state.target
                            || other == state.mapUid || Hostile(state.grid, state.group, other),
            length, returnOnFirstHit: true).Any();
    }

    /// <summary>Whether a crew may put rounds into a ship: one it is fighting, set on or warning off as hostile.</summary>
    private bool Hostile(EntityUid grid, string group, EntityUid other)
    {
        return _objectives.IsAttackTarget(grid, group, other) || _alerts.IsHostileShip(grid, group, other)
               || _security.IsHostileDockingTarget(grid, group, other);
    }
}
