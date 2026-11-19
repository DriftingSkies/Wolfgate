using System.Linq;
using Content.Server._WG.Planets;
using Content.Server.Parallax;
using Content.Server.Popups;
using Content.Shared._WG.Foraging;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Interaction;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Foraging;

/// <summary>A forage spot: picked by hand for what grows there; it withers if nobody picks it.</summary>
[RegisterComponent, Access(typeof(WGForagingSystem))]
public sealed partial class WGForageNodeComponent : Component
{
    [DataField(required: true)]
    public List<WGForageLoot> Loot = new();

    [DataField]
    public float PickTime = 2f;

    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromMinutes(25);

    [ViewVariables]
    public TimeSpan Withers;

    /// <summary>Already picked, so a second picker finishing at the same time gets nothing.</summary>
    [ViewVariables]
    public bool Picked;
}

/// <summary>
/// Foraging. Every minute, each planet map with players on it grows forage spots near a random player there, picked by
/// the world's outside temperature (<c>wgForageTable</c>), on open ground that isn't water, up to a cap a world. A spot
/// is picked by hand after a short gather and gives its loot; unpicked ones wither.
/// </summary>
public sealed class WGForagingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private WGPlanetClimateSystem _climate = default!;

    /// <summary>Forage spots a world holds at once.</summary>
    private const int Cap = 24;

    /// <summary>Spots grown per world each minute, at most.</summary>
    private const int PerTick = 2;

    private const float NearMin = 10f;
    private const float NearMax = 32f;

    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private static readonly SoundSpecifier Rustle = new SoundPathSpecifier("/Audio/Effects/Footsteps/grass1.ogg");
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGForageNodeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGForageNodeComponent, InteractHandEvent>(OnPick);
        SubscribeLocalEvent<WGForageNodeComponent, WGForageDoAfterEvent>(OnPicked);
    }

    private void OnMapInit(Entity<WGForageNodeComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Withers = _timing.CurTime + ent.Comp.Lifetime;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + Tick;

        // Wither what nobody picked.
        var counts = new Dictionary<EntityUid, int>();
        var nodes = EntityQueryEnumerator<WGForageNodeComponent, TransformComponent>();
        while (nodes.MoveNext(out var uid, out var node, out var xform))
        {
            if (_timing.CurTime >= node.Withers)
            {
                QueueDel(uid);
                continue;
            }

            if (xform.MapUid is { } map)
                counts[map] = counts.GetValueOrDefault(map) + 1;
        }

        // Players on planet worlds, by world.
        var players = new Dictionary<EntityUid, List<EntityCoordinates>>();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var actor, out _, out var xform))
        {
            // Ghosts and observers don't grow forage as they fly about.
            if (HasComp<GhostComponent>(actor) || !HasComp<MobStateComponent>(actor))
                continue;

            if (xform.MapUid is { } map && HasComp<BiomeComponent>(map))
            {
                if (!players.TryGetValue(map, out var list))
                    players[map] = list = new List<EntityCoordinates>();
                list.Add(xform.Coordinates);
            }
        }

        foreach (var (map, near) in players)
        {
            if (Table(map) is not { } table)
                continue;

            for (var i = 0; i < PerTick && counts.GetValueOrDefault(map) < Cap; i++)
            {
                if (TryGrow(map, _random.Pick(near), table))
                    counts[map] = counts.GetValueOrDefault(map) + 1;
            }
        }
    }

    private WGForageTablePrototype? Table(EntityUid map)
    {
        var celsius = _climate.GetOutsideTemperature(map) is { } kelvin ? kelvin - 273.15f : 18f;
        return _proto.EnumeratePrototypes<WGForageTablePrototype>()
            .OrderBy(t => t.ID)
            .FirstOrDefault(t => celsius >= t.MinTemp && celsius <= t.MaxTemp);
    }

    /// <summary>Grows one spot on open, dry, unblocked ground of the world near a player, if it finds such a tile.</summary>
    private bool TryGrow(EntityUid map, EntityCoordinates near, WGForageTablePrototype table)
    {
        if (!TryComp<MapGridComponent>(map, out var grid) || !TryComp<BiomeComponent>(map, out var biome) || table.Spots.Count == 0)
            return false;

        var angle = _random.NextFloat(0f, MathF.Tau);
        var distance = _random.NextFloat(NearMin, NearMax);
        var position = near.Position + new System.Numerics.Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
        var spot = new EntityCoordinates(near.EntityId, position);
        if (near.EntityId != map)
            return false;

        var indices = _maps.TileIndicesFor(map, grid, spot);
        if (!_maps.TryGetTileRef(map, grid, indices, out var tile) || tile.Tile.IsEmpty || _biomes.WgIsWater((map, biome), indices))
            return false;

        var centre = _maps.GridTileToLocal(map, grid, indices);
        foreach (var other in _lookup.GetEntitiesInRange(centre, 0.45f, LookupFlags.Static))
        {
            if (TryComp<PhysicsComponent>(other, out var body) && body.Hard && body.BodyType == BodyType.Static)
                return false;
        }

        var roll = _random.NextFloat() * table.Spots.Sum(s => s.Weight);
        var pick = table.Spots[^1].Id;
        foreach (var entry in table.Spots)
        {
            roll -= entry.Weight;
            if (roll > 0)
                continue;
            pick = entry.Id;
            break;
        }

        Spawn(pick, centre);
        return true;
    }

    private void OnPick(Entity<WGForageNodeComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.PickTime, new WGForageDoAfterEvent(), ent, ent)
            {
                BreakOnMove = true,
                NeedHand = true,
            }))
            _audio.PlayPvs(Rustle, ent);
    }

    private void OnPicked(Entity<WGForageNodeComponent> ent, ref WGForageDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Picked)
            return;

        args.Handled = true;
        ent.Comp.Picked = true;
        var where = Transform(ent).Coordinates;
        var found = 0;
        foreach (var loot in ent.Comp.Loot)
        {
            if (!_random.Prob(loot.Chance))
                continue;
            var count = _random.Next(loot.Min, loot.Max + 1);
            for (var i = 0; i < count; i++)
            {
                Spawn(loot.Id, where);
                found++;
            }
        }

        _popup.PopupEntity(Loc.GetString(found > 0 ? "wg-forage-picked" : "wg-forage-empty", ("spot", ent.Owner)), ent, args.User);
        QueueDel(ent);
    }
}
