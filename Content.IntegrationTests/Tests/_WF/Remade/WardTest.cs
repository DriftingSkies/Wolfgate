#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Ward;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Ward;
using Content.Shared._WF.ShipShields;
using Content.Shared.CCVar;
using Content.Shared.Projectiles;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The Ward gift: the stone as a ship shield generator, its dial, its climb, and what a rooted stone does.</summary>
[TestFixture]
public sealed class WardTest
{
    private const string Scion = "WFMonolithScion";

    private static EntityCoordinates At(TestMapData map, float x, float y) => WFRemadeDirectorTest.At(map, x, y);

    private static Entity<WFMonolithComponent> SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance)
    {
        var uid = entMan.SpawnEntity(Scion, at);
        entMan.RemoveComponent<WFRemadeDirectorComponent>(uid);
        var comp = entMan.GetComponent<WFMonolithComponent>(uid);
        comp.Resonance = resonance;
        return (uid, comp);
    }

    [Test]
    public async Task WardSetsHowHardTheShieldIsToBreak()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        Entity<WFMonolithComponent> scion = default;
        float fortress = 0f;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f);
            var biggest = entMan.SpawnEntity("ShieldGeneratorPOI", At(map, 8, 8));
            fortress = entMan.GetComponent<ShipShieldEmitterComponent>(biggest).DamageLimit;
            entMan.DeleteEntity(biggest);
            Assert.That(monoliths.GetDial(scion, WFGift.Ward), Is.Zero, "the Ward starts up.");
            monoliths.SetDial(scion, WFGift.Ward, 0.5f);
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            var emitter = entMan.GetComponent<ShipShieldEmitterComponent>(scion);
            var half = WFMonolithMath.WardStrength(0.5f, scion.Comp!.Resonance);
            Assert.That(emitter.DamageLimit, Is.EqualTo(half).Within(half * 0.01f), "the shield's strength isn't what the dial and resonance give.");
            Assert.That(emitter.DamageLimit, Is.GreaterThan(fortress), "the stone's shield is no stronger than the strongest ship generator.");
            Assert.That(emitter.Shield, Is.Not.Null, "the stone raised no field.");
            Assert.That(entMan.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Source, Is.EqualTo(scion.Owner));
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(scion).Load, Is.Zero, "the stone's shield draws power.");
            monoliths.SetDial(scion, WFGift.Ward, 1f);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var emitter = entMan.GetComponent<ShipShieldEmitterComponent>(scion);
            var full = WFMonolithMath.WardStrength(1f, scion.Comp!.Resonance);
            Assert.That(emitter.DamageLimit, Is.EqualTo(full).Within(full * 0.01f));
            Assert.That(emitter.HealPerSecond, Is.EqualTo(WFMonolithMath.WardMendRate(1f, scion.Comp!.Resonance)).Within(50f));
            scion.Comp!.Resonance = 60f;
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var emitter = entMan.GetComponent<ShipShieldEmitterComponent>(scion);
            Assert.That(emitter.DamageLimit, Is.GreaterThan(WFMonolithMath.WardStrength(1f, 50f)), "resonance added nothing to the shield.");

            monoliths.SetDial(scion, WFGift.Ward, 0f);
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ShipShieldEmitterComponent>(scion).Shield, Is.Null, "the field stayed up with the dial off.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WardAddsToTheClimb()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        Entity<WFMonolithComponent> plain = default, warded = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            plain = SpawnScion(entMan, At(map, -15, 0), 0f);
            warded = SpawnScion(entMan, At(map, 15, 0), 0f);
            monoliths.SetDial(plain, WFGift.Power, 0.5f);
            monoliths.SetDial(warded, WFGift.Power, 0.5f);
            monoliths.SetDial(warded, WFGift.Ward, 0.5f);
            Assert.That(monoliths.EffectiveDraw(warded), Is.GreaterThan(monoliths.EffectiveDraw(plain)));
        });
        await pair.RunSeconds(60f);

        await server.WaitAssertion(() =>
        {
            Assert.That(plain.Comp!.Resonance, Is.GreaterThan(0f));
            Assert.That(warded.Comp!.Resonance, Is.GreaterThan(plain.Comp!.Resonance * 1.4f),
                $"a Ward dial climbed {warded.Comp!.Resonance} against {plain.Comp!.Resonance} without it.");
            entMan.DeleteEntity(plain);
            entMan.DeleteEntity(warded);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStationBarsTheWard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        Entity<WFMonolithComponent> scion = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            entMan.EnsureComponent<Content.Server._WF.Remade.Corruption.WFRemadeSanctuaryComponent>(map.Grid.Owner);
            scion = SpawnScion(entMan, At(map, 0, 0), 100f);
            monoliths.SetDial(scion, WFGift.Ward, 1f);
        });
        await pair.RunSeconds(5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(monoliths.IsBarred(scion), Is.True, "the test grid isn't a sanctuary.");
            Assert.That(monoliths.GetDial(scion, WFGift.Ward), Is.Zero);
            Assert.That(entMan.GetComponent<ShipShieldEmitterComponent>(scion).Shield, Is.Null, "a stone on a sanctuary grid raised a field.");
            Assert.That(entMan.System<WFWardSystem>().IsWalled(scion), Is.False, "a stone on a sanctuary grid raised a wall.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns a small dynamic ship at a world position, flying at a velocity.</summary>
    private static EntityUid SpawnShip(TestPair pair, TestMapData map, Vector2 at, Vector2 velocity)
    {
        var entMan = pair.Server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map.MapId);
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), map.Tile.Tile);

        var physics = entMan.System<SharedPhysicsSystem>();
        var body = entMan.GetComponent<PhysicsComponent>(grid.Owner);
        physics.SetBodyType(grid.Owner, BodyType.Dynamic, body: body);
        physics.SetFixedRotation(grid.Owner, false, body: body);
        entMan.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, at);
        physics.SetLinearVelocity(grid.Owner, velocity, body: body);
        return grid.Owner;
    }

    [Test]
    public async Task ARootedStoneStopsShipsAndShipGunsButLetsPeopleCross()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        Entity<WFMonolithComponent> scion = default;
        EntityUid ship = default, walker = default, round = default;
        var radius = 0f;

        await server.WaitPost(() =>
        {
            // Friction would damp a ship to a stop in the vacuum.
            server.CfgMan.SetCVar(CCVars.MinFriction, 0f);
            server.CfgMan.SetCVar(CCVars.AirFriction, 0f);
            server.CfgMan.SetCVar(CCVars.OffgridFriction, 0f);
            WFRemadeDirectorTest.LayDeck(pair, map);
            // Rapture: a field, but no wall yet.
            scion = SpawnScion(entMan, At(map, 0, 0), 90f);
            entMan.System<WFMonolithSystem>().SetDial(scion, WFGift.Ward, 1f);
        });
        await pair.RunSeconds(4f);

        await server.WaitPost(() =>
        {
            var emitter = entMan.GetComponent<ShipShieldEmitterComponent>(scion);
            Assert.That(emitter.Shield, Is.Not.Null, "no field to measure.");
            var contours = entMan.GetComponent<WFShipShieldVisualsComponent>(emitter.Shield!.Value).Contours;
            radius = contours.SelectMany(contour => contour).Max(point => point.X);
            Assert.That(entMan.System<WFWardSystem>().IsWalled(scion), Is.False, "a stone short of the Crescendo raised a wall.");
            ship = SpawnShip(pair, map, new Vector2(radius + 25f, 0f), new Vector2(-8f, 0f));
        });
        await pair.RunSeconds(12f);

        await server.WaitAssertion(() =>
        {
            var x = transforms.GetWorldPosition(ship).X;
            Assert.That(x, Is.LessThan(radius - 3f), $"the control ship didn't fly into an unwalled shield (x {x}, shield {radius}).");
            transforms.SetWorldPosition(ship, new Vector2(radius + 25f, 0f));
            physics.SetLinearVelocity(ship, new Vector2(-8f, 0f));
            physics.SetAngularVelocity(ship, 0f);

            // Now it roots.
            scion.Comp!.Resonance = 100f;
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(scion.Comp!.Stage, Is.EqualTo(WFMonolithStage.Crescendo));
            var emitter = entMan.GetComponent<ShipShieldEmitterComponent>(scion);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(emitter.DamageLimit, Is.EqualTo(WFMonolithMath.WardRootedCapacity));
            var ward = entMan.GetComponent<WFWardComponent>(scion);
            Assert.That(entMan.System<WFWardSystem>().IsWalled(scion), Is.True, "a rooted stone raised no wall.");
            Assert.That(ward.Friendly, Does.Contain(map.Grid.Owner));
            Assert.That(ward.Friendly, Does.Not.Contain(ship));
            transforms.SetWorldPosition(ship, new Vector2(radius + 25f, 0f));
            physics.SetLinearVelocity(ship, new Vector2(-8f, 0f));
            physics.SetAngularVelocity(ship, 0f);
            walker = entMan.SpawnEntity("MobCarp", new EntityCoordinates(map.MapUid, new Vector2(radius + 6f, 6f)));
            physics.SetLinearVelocity(walker, new Vector2(-12f, 0f));

            // A ship's shell fired at the stone from outside.
            round = entMan.SpawnEntity("BulletDebugZoom", new EntityCoordinates(map.MapUid, new Vector2(-radius - 40f, 0f)));
            entMan.EnsureComponent<ShipWeaponProjectileComponent>(round);
            physics.SetLinearVelocity(round, new Vector2(120f, 0f));
            entMan.GetComponent<ShipShieldEmitterComponent>(scion).Damage = 5_000_000f;
        });
        await pair.RunSeconds(12f);

        await server.WaitAssertion(() =>
        {
            var x = transforms.GetWorldPosition(ship).X;
            Assert.That(x, Is.GreaterThan(radius - 1f), $"a second ship flew inside a rooted stone's shield (x {x}, shield {radius}, turned back {entMan.GetComponent<WFWardComponent>(scion).Turned} times).");
            Assert.That(transforms.GetWorldPosition(walker).X, Is.LessThan(radius - 2f), "a mob couldn't cross a rooted stone's shield.");
            Assert.That(entMan.Deleted(round) || entMan.GetComponent<ProjectileComponent>(round).ProjectileSpent, Is.True,
                "a ship's shell got through a rooted stone's shield.");

            var emitter = entMan.GetComponent<ShipShieldEmitterComponent>(scion);
            Assert.That(emitter.Shield, Is.Not.Null, "the rooted stone's field fell.");
            Assert.That(emitter.Damage, Is.Zero, "a rooted stone's shield kept damage.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheStonesFieldIsMarkedForClientsAndAnOrdinaryShipFieldIsNot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        Entity<WFMonolithComponent> scion = default;
        EntityUid ordinary = default, ordinaryShield = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f);
            monoliths.SetDial(scion, WFGift.Ward, 0.5f);

            // A plain ship generator on a ship of its own.
            var maps = entMan.System<SharedMapSystem>();
            var ship = maps.CreateGridEntity(map.MapId);
            entMan.System<SharedTransformSystem>().SetLocalPosition(ship.Owner, new Vector2(300f, 0f));
            for (var x = 0; x < 4; x++)
            for (var y = 0; y < 4; y++)
                maps.SetTile(ship.Owner, ship.Comp, new Vector2i(x, y), map.Tile.Tile);

            ordinary = entMan.SpawnEntity("ShieldGeneratorPOI", new EntityCoordinates(ship.Owner, new Vector2(1.5f, 1.5f)));
        });
        await pair.RunSeconds(5f);

        EntityUid shield = default;
        await server.WaitAssertion(() =>
        {
            shield = entMan.GetComponent<ShipShieldEmitterComponent>(scion).Shield ?? default;
            Assert.That(shield, Is.Not.EqualTo(default(EntityUid)), "the stone raised no field.");
            var marked = entMan.GetComponent<WFWardFieldComponent>(shield);
            Assert.That(marked.Stage, Is.EqualTo(WFMonolithStage.Hymn));
            Assert.That(marked.Rooted, Is.False);

            ordinaryShield = entMan.GetComponent<ShipShieldEmitterComponent>(ordinary).Shield ?? default;
            if (ordinaryShield != default)
                Assert.That(entMan.HasComponent<WFWardFieldComponent>(ordinaryShield), Is.False, "an ordinary ship generator's field is marked as the Ward's.");

            scion.Comp!.Resonance = 100f;
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            var field = entMan.GetComponent<ShipShieldEmitterComponent>(scion).Shield ?? default;
            var marked = entMan.GetComponent<WFWardFieldComponent>(field);
            Assert.That(marked.Stage, Is.EqualTo(WFMonolithStage.Crescendo));
            Assert.That(marked.Rooted, Is.True, "the rooted stone's field isn't marked rooted.");
            shield = field;
        });

        await client.WaitAssertion(() =>
        {
            var seen = client.EntMan.GetComponent<WFWardFieldComponent>(pair.ToClientUid(shield));
            Assert.That(seen.Rooted, Is.True, "the client doesn't know the field is rooted.");
            Assert.That(seen.Stage, Is.EqualTo(WFMonolithStage.Crescendo));
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(scion);
            entMan.DeleteEntity(ordinary);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheWardsShaderParsesAndItsHardnessLooksHarder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var protos = client.ProtoMan;
            var shader = protos.Index<Robust.Client.Graphics.ShaderPrototype>("WFRemadeWardField");
            Assert.That(shader, Is.Not.Null);

            // The headless client compiles no GL, but Robust's own shader parser runs, which catches bad syntax and unknown types.
            var resources = client.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();
            var parser = typeof(Robust.Client.Graphics.ShaderPrototype).Assembly.GetType("Robust.Client.Graphics.ShaderParser")!;
            var parse = parser.GetMethod("Parse", new[] { typeof(string), typeof(Robust.Shared.ContentPack.IResourceManager) })!;
            foreach (var path in new[] { "/Textures/_WF/Remade/Shaders/ward.swsl", "/Textures/_WF/Remade/Shaders/dread.swsl" })
            {
                using var stream = resources.ContentFileRead(path);
                using var reader = new System.IO.StreamReader(stream);
                var code = reader.ReadToEnd();
                try
                {
                    parse.Invoke(null, new object[] { code, resources });
                }
                catch (System.Reflection.TargetInvocationException e)
                {
                    Assert.Fail($"{path} doesn't parse: {e.InnerException?.Message}");
                }
            }

            // The field looks harder as the stone climbs, and fully hard only once rooted.
            var loose = new WFWardFieldComponent { Stage = WFMonolithStage.Growth };
            var harvest = new WFWardFieldComponent { Stage = WFMonolithStage.Harvest };
            var rooted = new WFWardFieldComponent { Stage = WFMonolithStage.Crescendo, Rooted = true };
            Assert.That(Content.Client._WF.Remade.Ward.WFWardOverlay.Hardness(harvest), Is.GreaterThan(Content.Client._WF.Remade.Ward.WFWardOverlay.Hardness(loose)));
            Assert.That(Content.Client._WF.Remade.Ward.WFWardOverlay.Hardness(rooted), Is.EqualTo(1f));
            Assert.That(Content.Client._WF.Remade.Ward.WFWardOverlay.Hardness(harvest), Is.LessThan(1f));
        });
        await pair.CleanReturnAsync();
    }
}
