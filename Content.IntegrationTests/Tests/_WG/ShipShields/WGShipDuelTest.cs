#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.FireControl;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>
/// Not a check: two real hulls face each other and fire every gun they carry, with their own ammo, shields and the NPC
/// vent rule, and the run prints how heat, collapses, vents and gun locks went. Run by hand when tuning ship heat.
/// </summary>
[TestFixture, Explicit]
public sealed class WGShipDuelTest
{
    private const float Seconds = 150f;
    private const float Step = 0.25f;
    private const float Gap = 40f;

    private static readonly object[] Pairs =
    {
        new object[] { "TSF/banshee", "TSF/cinquedea" },
        new object[] { "TSF/mercury", "PDV/layak" },
        new object[] { "TSF/polaris", "TSF/luda" },
        new object[] { "PDV/kresta", "TSF/dagger" },
        new object[] { "PDV/osa", "TSF/paladin" },
        // Faction matchups by class: TSF against PDV, and the civilian military hulls.
        new object[] { "TSF/qj271", "PDV/kynda" },
        new object[] { "TSF/seax", "PDV/gadfly" },
        new object[] { "TSF/paladin", "PDV/ontos" },
        new object[] { "TSF/ocelot", "PDV/kresta" },
        new object[] { "CivilianMil/qj270", "CivilianMil/qj490" },
        new object[] { "CivilianMil/talon", "TSF/shiv" },
        new object[] { "CivilianMil/sellsword", "TSF/xiphos" },
        new object[] { "TSF/banshee", "PDV/tethys" },
        new object[] { "TSF/fracture", "CivilianMil/femur" },
        new object[] { "TSF/dagger", "CivilianMil/kunai" },
        new object[] { "TSF/mercury", "PDV/neptune" },
        new object[] { "TSF/polaris", "PDV/kortik_r" },
        new object[] { "CivilianMil/xinhua", "PDV/osa" },
        new object[] { "TSF/luda", "PDV/scorpion" },
    };

    [TestCaseSource(nameof(Pairs))]
    public Task Duel(string first, string second)
    {
        return Run(first, second, 0f);
    }

    /// <summary>The same duels with a bare hull's built-in sink scaled by its tiles instead of flat.</summary>
    [TestCaseSource(nameof(Pairs))]
    public Task DuelTileSink(string first, string second)
    {
        return Run(first, second, 70f);
    }

    /// <summary>Runs one duel; a <paramref name="sinkPerTile"/> above 0 sets each hull's sink to that much per tile.</summary>
    private static async Task Run(string first, string second, float sinkPerTile)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var shields = entities.System<ShipShieldsSystem>();
        var fire = entities.System<FireControlSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var ships = new Ship[2];

        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var loader = entities.System<MapLoaderSystem>();
            maps.CreateMap(out var mapId);
            var paths = new[] { first, second };
            for (var i = 0; i < 2; i++)
            {
                Assert.That(loader.TryLoadGrid(mapId, new ResPath($"/SharedMaps/_Mono/Shuttles/{paths[i]}.yml"), out var grid), Is.True);
                ships[i] = new Ship(Path.GetFileName(paths[i]), grid!.Value.Owner);
            }

            // Noses face each other across the gap: the first points north, the second south.
            var a = entities.GetComponent<MapGridComponent>(ships[0].Grid).LocalAABB;
            var b = entities.GetComponent<MapGridComponent>(ships[1].Grid).LocalAABB;
            transform.SetWorldPositionRotation(ships[0].Grid, Vector2.Zero, Angle.Zero);
            transform.SetWorldPositionRotation(ships[1].Grid, new Vector2(a.Center.X + b.Center.X, a.Top + Gap + b.Top), Angle.FromDegrees(180));

            // Power is not what this measures: every machine on both hulls runs.
            var receivers = entities.System<SharedPowerReceiverSystem>();
            var powered = entities.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
            while (powered.MoveNext(out var uid, out var receiver, out var xform))
            {
                if (xform.GridUid == ships[0].Grid || xform.GridUid == ships[1].Grid)
                    receivers.SetNeedsPower(uid, false, receiver);
            }

            var guns = entities.EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
            while (guns.MoveNext(out var uid, out _, out var xform))
            {
                foreach (var ship in ships)
                {
                    if (xform.GridUid == ship.Grid)
                        ship.Guns.Add(uid);
                }
            }
            var emitters = entities.EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
            while (emitters.MoveNext(out var uid, out _, out var xform))
            {
                foreach (var ship in ships)
                {
                    if (xform.GridUid == ship.Grid && xform.Anchored)
                        ship.Generator ??= uid;
                }
            }
        });

        // Let the fields come up before the first shot.
        await pair.RunSeconds(4f);
        await server.WaitPost(() =>
        {
            foreach (var ship in ships)
            {
                ship.Tiles = CountTiles(entities, ship.Grid);
                if (sinkPerTile > 0f)
                    entities.EnsureComponent<WGShipHeatComponent>(ship.Grid).SinkCapacity = ship.Tiles * sinkPerTile;
                ship.Capacity = shields.GetWolfgateShipHeat(ship.Grid).Capacity;
            }
        });

        var log = new StringBuilder("t,ship,heat,hard,field,venting,held,locked,generator,tiles\n");
        var steps = (int) (Seconds / Step);
        for (var step = 0; step < steps; step++)
        {
            var time = step * Step;
            await server.WaitPost(() =>
            {
                for (var i = 0; i < 2; i++)
                {
                    var ship = ships[i];
                    var target = ships[1 - i];
                    if (entities.Deleted(ship.Grid) || entities.Deleted(target.Grid))
                        continue;
                    // A slugging match: neither hull drifts away from the other.
                    physics.SetLinearVelocity(ship.Grid, Vector2.Zero);
                    physics.SetAngularVelocity(ship.Grid, 0f);
                    var aim = new EntityCoordinates(entities.GetComponent<TransformComponent>(target.Grid).MapUid!.Value,
                        Vector2.Transform(entities.GetComponent<MapGridComponent>(target.Grid).LocalAABB.Center,
                            transform.GetWorldMatrix(target.Grid)));
                    // The NPC crew rule: no firing into a raised field that is nearly full.
                    if (shields.WantsWolfgateNpcHoldFire(ship.Grid))
                        continue;
                    foreach (var gun in ship.Guns)
                    {
                        if (!entities.Deleted(gun) && fire.AttemptFire(gun, gun, aim, noServer: true))
                            ship.Volleys++;
                    }
                }
            });
            await pair.RunSeconds(Step);
            if (step % (int) (1f / Step) != 0)
                continue;
            await server.WaitPost(() =>
            {
                foreach (var ship in ships)
                {
                    if (entities.Deleted(ship.Grid))
                        continue;
                    // The NPC crew rule a player-less ship follows.
                    if (shields.WantsWolfgateNpcVent(ship.Grid, true))
                        shields.StartWolfgateVent(ship.Grid);
                    Record(entities, shields, ship, time, log);
                }
            });
        }

        await server.WaitPost(() =>
        {
            foreach (var ship in ships)
                ship.TilesLeft = entities.Deleted(ship.Grid) ? 0 : CountTiles(entities, ship.Grid);
        });

        var variant = sinkPerTile > 0f ? $"sink {sinkPerTile}/tile" : "flat sink";
        var summary = new StringBuilder($"{ships[0].Name} vs {ships[1].Name}, {Seconds} s, {variant}\n");
        foreach (var ship in ships)
            summary.AppendLine(ship.Summary());
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ship_duels");
        Directory.CreateDirectory(dir);
        var stem = Path.Combine(dir, $"{ships[0].Name}_vs_{ships[1].Name}{(sinkPerTile > 0f ? "_tilesink" : "")}");
        await File.WriteAllTextAsync(stem + ".csv", log.ToString());
        await File.WriteAllTextAsync(stem + ".txt", summary.ToString());
        TestContext.Out.WriteLine(summary.ToString());
        TestContext.Out.WriteLine($"Log: {stem}.csv");

        await pair.CleanReturnAsync();
    }

    private static int CountTiles(IEntityManager entities, EntityUid grid)
    {
        return entities.System<SharedMapSystem>().GetAllTiles(grid, entities.GetComponent<MapGridComponent>(grid)).Count();
    }

    private static void Record(IEntityManager entities, ShipShieldsSystem shields, Ship ship, float time, StringBuilder log)
    {
        var (heat, capacity) = shields.GetWolfgateShipHeat(ship.Grid);
        var hard = entities.TryGetComponent<WGShipHeatComponent>(ship.Grid, out var hull) ? hull.Hard : 0f;
        var emitter = ship.Generator is { } generator && entities.TryGetComponent<ShipShieldEmitterComponent>(generator, out var found) ? found : null;
        var field = emitter?.Shield != null;
        var venting = shields.IsWolfgateShipVenting(ship.Grid);
        var held = shields.IsWolfgateShipHeatHeld(ship.Grid);
        var locked = shields.GetWolfgateGunLockSeconds(ship.Grid) > 0;
        var share = capacity > 0f ? heat / capacity : 0f;

        if (emitter != null)
        {
            if (time > 0f && ship.Field && !field)
            {
                if (venting)
                    ship.Lowered++;
                else
                {
                    ship.Collapses++;
                    ship.FirstCollapse ??= time;
                }
            }
            else if (!ship.Field && field && time > 0f)
                ship.Returns.Add(share);
            ship.Field = field;
            if (!field)
                ship.FieldDown++;
        }
        if (venting && !ship.Venting)
            ship.Vents++;
        ship.Venting = venting;
        if (venting)
            ship.VentSeconds++;
        if (held)
        {
            ship.HeldSeconds++;
            ship.FirstHeld ??= time;
        }
        if (locked)
            ship.LockedSeconds++;
        if (!venting && !held && !locked)
            ship.FreeSeconds++;
        ship.PeakHeat = MathF.Max(ship.PeakHeat, share);
        // A generator shot off its hull or destroyed no longer reads as a collapse, so it is logged on its own.
        var generatorAlive = ship.Generator is { } installed && !entities.Deleted(installed) &&
            entities.GetComponent<TransformComponent>(installed) is { Anchored: true } placed && placed.GridUid == ship.Grid;
        if (ship.Generator != null && !generatorAlive)
            ship.GeneratorLost ??= time;
        var tiles = CountTiles(entities, ship.Grid);
        log.Append($"{time:0},{ship.Name},{share:0.000},{(capacity > 0f ? hard / capacity : 0f):0.000},{(field ? 1 : 0)},{(venting ? 1 : 0)},{(held ? 1 : 0)},{(locked ? 1 : 0)},{(generatorAlive ? 1 : 0)},{tiles}\n");
    }

    private sealed class Ship
    {
        public readonly string Name;
        public readonly EntityUid Grid;
        public readonly List<EntityUid> Guns = new();
        public readonly List<float> Returns = new();
        public EntityUid? Generator;
        public int Tiles, TilesLeft, Volleys, Collapses, Lowered, Vents, VentSeconds, HeldSeconds, LockedSeconds, FreeSeconds, FieldDown;
        public float? FirstCollapse, FirstHeld, GeneratorLost;
        public float Capacity;
        public float PeakHeat;
        public bool Field = true, Venting;

        public Ship(string name, EntityUid grid)
        {
            Name = name;
            Grid = grid;
        }

        public string Summary()
        {
            var returns = Returns.Count == 0 ? "none" : string.Join(" ", Returns.Select(r => $"{r:P0}"));
            return $"  {Name}: {Guns.Count} guns, {Volleys} trigger pulls; generator {(Generator == null ? "none" : GeneratorLost is { } lost ? $"lost at {lost:0} s" : "yes")}; " +
                $"first collapse {(FirstCollapse is { } t ? $"{t:0} s" : "never")}, {Collapses} collapses, {Lowered} lowered to vent, field down {FieldDown} s, " +
                $"returned at heat {returns}; {Vents} vents ({VentSeconds} s); capacity {Capacity:0}; guns held by heat {HeldSeconds} s " +
                $"(first {(FirstHeld is { } h ? $"{h:0} s" : "never")}), locked {LockedSeconds} s, " +
                $"free {FreeSeconds} s; peak heat {PeakHeat:P0}; tiles {Tiles} -> {TilesLeft}";
        }
    }
}
