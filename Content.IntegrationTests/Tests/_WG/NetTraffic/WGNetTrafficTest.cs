using System.Numerics;
using Content.Server._WG.NetTraffic;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WG.NetTraffic;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using ClientShells = Content.Client._WG.NetTraffic.WGNetShellSystem;
using ClientExplosions = Content.Client._WG.NetTraffic.WGNetExplosionSystem;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

/// <summary>Ship shells and explosions reach clients as messages, and a structure's damage is sent at most once an interval.</summary>
[TestFixture]
public sealed class WGNetTrafficTest
{
    [Test]
    public async Task ShellIsDrawnFromMessagesNotSent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        // The pool runs with PVS off, which sends every entity.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await pair.RunTicksSync(5);
        EntityUid shell = default;
        NetEntity shellNet = default;

        await server.WaitAssertion(() =>
        {
            var viewer = em.SpawnEntity(null, map.MapCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
            shell = em.SpawnEntity("57mmBulletHE", new MapCoordinates(new Vector2(5, 0), map.MapId));
            shellNet = em.GetNetEntity(shell);
            Assert.That(em.HasComponent<WGNetShellComponent>(shell), "An unguided ship shell is kept off the network.");
            var vis = em.GetComponent<VisibilityComponent>(shell);
            Assert.That((vis.Layer & WGNetTraffic.NeverSentLayer) != 0, "It sits on the layer no eye sees.");
            var physics = em.GetComponent<PhysicsComponent>(shell);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(shell, new Vector2(1, 0), body: physics);
        });

        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.EntMan.TryGetEntity(shellNet, out _), Is.False, "The client is never sent the shell.");
            Assert.That(pair.Client.System<ClientShells>().Count, Is.EqualTo(1), "The client draws its own copy.");
        });

        await server.WaitPost(() => em.DeleteEntity(shell));
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
            Assert.That(pair.Client.System<ClientShells>().Count, Is.Zero, "The copy goes when the shell does."));

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, false));
        await pair.CleanReturnAsync();
    }

    /// <summary>A player watching through a far camera is told of shells by the camera, not by where they stand.</summary>
    [Test]
    public async Task ShellReachesAPlayerWatchingThroughACamera()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var viewer = em.SpawnEntity(null, map.MapCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
            var far = new MapCoordinates(new Vector2(20000, 0), map.MapId);
            var camera = em.SpawnEntity(null, far);
            server.System<Robust.Server.GameObjects.ViewSubscriberSystem>().AddViewSubscriber(camera, pair.Player!);
            var shell = em.SpawnEntity("57mmBulletHE", far);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(shell, new Vector2(1, 0),
                body: em.GetComponent<PhysicsComponent>(shell));
        });

        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
            Assert.That(pair.Client.System<ClientShells>().Count, Is.EqualTo(1), "The camera's view counts."));

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, false));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplosionArrivesAsOneMessage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        // The pool runs with PVS off, which sends every entity.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var viewer = em.SpawnEntity(null, map.MapCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
            server.System<ExplosionSystem>().QueueExplosion(map.GridCoords.ToMap(em, server.System<SharedTransformSystem>()),
                "Default", 18, 2, 2, null, maxTileBreak: 0);
        });

        // Watch for the overlay while it shows; it is gone once its persistence runs out.
        var seen = false;
        for (var i = 0; i < 20 && !seen; i++)
        {
            await pair.RunTicksSync(1);
            await pair.Client.WaitPost(() => seen = pair.Client.System<ClientExplosions>().Count > 0);
        }

        Assert.That(seen, "The client builds the overlay from the message.");
        await pair.Client.WaitAssertion(() =>
        {
            var query = pair.Client.EntMan.EntityQueryEnumerator<Content.Shared.Explosion.Components.ExplosionVisualsComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out _))
            {
                Assert.That(pair.Client.EntMan.IsClientSide(uid), "No server visuals entity reaches the client.");
            }
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, false));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StructureDamageIsSentAtMostOnceAnInterval()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        EntityUid wall = default;
        GameTickHolder first = new();

        DamageSpecifier blunt = default!;
        await server.WaitAssertion(() =>
        {
            wall = em.SpawnEntity("WallSolid", map.GridCoords);
            Assert.That(em.GetComponent<TransformComponent>(wall).Anchored, "Only anchored structures are held back.");
            blunt = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 1);
            server.System<DamageableSystem>().TryChangeDamage(wall, blunt, true);
            first.Tick = em.GetComponent<DamageableComponent>(wall).LastModifiedTick;
        });

        // The second hit lands on a later tick, still inside the interval.
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            server.System<DamageableSystem>().TryChangeDamage(wall, blunt, true);
            Assert.That(em.GetComponent<DamageableComponent>(wall).LastModifiedTick, Is.EqualTo(first.Tick),
                "A second hit inside the interval isn't sent at once.");
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(em.GetComponent<DamageableComponent>(wall).LastModifiedTick, Is.EqualTo(first.Tick)));

        // The default interval is a second.
        await pair.RunTicksSync(40);
        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<DamageableComponent>(wall);
            Assert.That(comp.LastModifiedTick, Is.GreaterThan(first.Tick), "The held change is sent once the interval is up.");
            Assert.That(comp.TotalDamage.Float(), Is.EqualTo(2f), "The server's damage was never held back.");
        });

        await pair.CleanReturnAsync();
    }

    private sealed class GameTickHolder
    {
        public Robust.Shared.Timing.GameTick Tick;
    }
}
