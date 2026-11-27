using Content.Server._WG.Planets.Atmosphere;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._ES.TileFires;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.GroundFire;

/// <summary>
/// Keeps a tile fire on planet ground to a small patch that burns out. Upstream tile fires spread to every flammable
/// tile for as long as there is air, and a planet never runs out of it.
/// </summary>
public sealed class WGGroundFireSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Most tile fires one fire and all it starts may make, itself included.</summary>
    public const int MaxFires = 12;

    /// <summary>Most tile fires one planet ground may have burning at once, however they were lit.</summary>
    public const int MaxFiresPerGround = 48;

    /// <summary>A fire lit this close to a spreading ground fire, in tiles, joins its line instead of starting a new one.</summary>
    public const float JoinRange = 4f;

    /// <summary>How long a fire on planet ground burns before it is put out for good.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(100);

    private EntityQuery<WGTerrainAtmosphereComponent> _terrainQuery;
    private readonly HashSet<Entity<WGGroundFireComponent>> _nearby = new();

    /// <summary>Tile fires on each planet ground, counted once a tick and kept up as fires are lit within it.</summary>
    private readonly Dictionary<EntityUid, int> _burning = new();
    private GameTick _countedTick;

    public override void Initialize()
    {
        SubscribeLocalEvent<WGGroundFireComponent, EntityTerminatingEvent>(OnTerminating);
        _terrainQuery = GetEntityQuery<WGTerrainAtmosphereComponent>();
    }

    /// <summary>Whether a new fire may be lit on this grid; planet ground refuses one once it has its fill.</summary>
    public bool CanIgnite(EntityUid grid)
    {
        if (!_terrainQuery.HasComp(grid))
            return true;

        if (Burning(grid) >= MaxFiresPerGround)
            return false;

        _burning[grid]++;
        return true;
    }

    /// <summary>Whether a fire may start another one. Fires off planet ground are only limited on a no-spread grid.</summary>
    public bool CanSpread(Entity<ESTileFireComponent> fire)
    {
        if (Transform(fire).GridUid is not { } grid)
            return true;

        if (HasComp<WGNoFireSpreadComponent>(grid))
            return false;

        if (!_terrainQuery.HasComp(grid))
            return true;

        if (!TryComp(fire, out WGGroundFireComponent? ground))
        {
            // Sparks or a blast landing beside a fire add to it, not a fresh line of their own.
            var budget = JoinsLine(fire, grid) ? 0 : MaxFires - 1;
            ground = AddComp<WGGroundFireComponent>(fire);
            ground.Budget = budget;
            Limit(fire.Comp, _timing.CurTime + Lifetime);
        }

        return ground.Budget > 0 && Burning(grid) < MaxFiresPerGround;
    }

    /// <summary>Hands a share of the parent's budget to a fire it just started.</summary>
    public void Spread(Entity<ESTileFireComponent> parent, EntityUid child)
    {
        if (!TryComp(parent, out WGGroundFireComponent? ground) || ground.Budget <= 0)
            return;

        if (Transform(parent).GridUid is { } grid && _terrainQuery.HasComp(grid))
        {
            Burning(grid);
            _burning[grid]++;
        }

        var share = (ground.Budget - 1) / 2;
        ground.Budget -= 1 + share;
        AddComp<WGGroundFireComponent>(child).Budget = share;

        // The whole line goes out together.
        if (TryComp(child, out ESTileFireComponent? fire))
            Limit(fire, parent.Comp.SmolderTime);
    }

    /// <summary>Whether another fire on this ground that already holds a budget burns within <see cref="JoinRange"/>.</summary>
    private bool JoinsLine(EntityUid fire, EntityUid grid)
    {
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(fire).Coordinates, JoinRange, _nearby);
        foreach (var other in _nearby)
        {
            if (other.Owner != fire && Transform(other).GridUid == grid)
                return true;
        }

        return false;
    }

    /// <summary>Tile fires burning on a planet ground, recounted on the first call each tick.</summary>
    private int Burning(EntityUid grid)
    {
        if (_countedTick != _timing.CurTick)
        {
            _countedTick = _timing.CurTick;
            _burning.Clear();
            var query = EntityQueryEnumerator<ESTileFireComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid is { } ground && _terrainQuery.HasComp(ground))
                    _burning[ground] = _burning.GetValueOrDefault(ground) + 1;
            }
        }

        return _burning.TryAdd(grid, 0) ? 0 : _burning[grid];
    }

    private static void Limit(ESTileFireComponent fire, TimeSpan end)
    {
        if (end < fire.SmolderTime)
            fire.SmolderTime = end;

        fire.SmolderDeleteFireChance = 1f;
    }

    private void OnTerminating(Entity<WGGroundFireComponent> ent, ref EntityTerminatingEvent args)
    {
        var xform = Transform(ent);

        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;

        var tile = _map.TileIndicesFor(grid, mapGrid, xform.Coordinates);

        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            _atmos.WgRestoreBurntAir(grid, tile + new Vector2i(x, y));
        }
    }
}
