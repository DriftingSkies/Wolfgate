using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Lattice;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade.Ward;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>Counts the breaches announced.</summary>
public sealed class WFLatticeBreachObserver : EntitySystem
{
    public int Breaches;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent((ref WFLatticeBreachEvent _) => Breaches++);
    }
}

/// <summary>Anchor stations are built shielded, a destroyed core is a breach, and a parked relay takes hold.</summary>
public sealed class WFLatticeStationTest
{
    [Test]
    public async Task StationsBreachesAndRelays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var damage = entities.System<DamageableSystem>();

        var stations = new List<EntityUid>();
        EntityUid relay = default;
        await server.WaitAssertion(() =>
        {
            entities.System<WFLatticeBreachObserver>().Breaches = 0;
            // Lost anchors let go at once here, so the counts below are exact.
            server.CfgMan.SetCVar(WFLatticeEndgameCVars.AnchorFade, 0f);
            lattice.Setup(map.MapUid);
            var query = entities.EntityQueryEnumerator<WFLatticeAnchorStationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    stations.Add(uid);
            }

            Assert.That(stations, Has.Count.EqualTo(6));
            foreach (var station in stations)
            {
                var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
                Assert.That(entities.GetComponent<WFLatticeAnchorComponent>(comp.Core).Primary, Is.True);
                Assert.That(entities.GetComponent<PowerSupplierComponent>(comp.Core).MaxSupply, Is.GreaterThanOrEqualTo(100000f),
                    "an anchor core doesn't power its station.");
                Assert.That(entities.GetComponent<Content.Shared.NodeContainer.NodeContainerComponent>(comp.Core).Nodes.ContainsKey("output"), Is.True);
                var shielded = entities.EntityQuery<ShipShieldEmitterComponent>()
                    .Any(emitter => entities.GetComponent<TransformComponent>(emitter.Owner).GridUid == station);
                Assert.That(shielded, Is.True, "an anchor station has no shield generator.");
                var generator = entities.EntityQuery<WFLatticeAnchorShieldComponent>()
                    .First(shield => entities.GetComponent<TransformComponent>(shield.Owner).GridUid == station).Owner;
                Assert.That(entities.HasComponent<Content.Shared.Construction.Components.AnchorableComponent>(generator), Is.False, "an anchor's shield generator can be unbolted.");
                var construction = entities.GetComponent<Content.Server.Construction.Components.ConstructionComponent>(generator);
                Assert.That(construction.Graph, Is.EqualTo("WFLatticeAnchorShield"), "an anchor's shield generator can be taken apart.");
                Assert.That(construction.DeconstructionNode, Is.Null, "an anchor's shield generator can be taken apart.");
                Assert.That(entities.HasComponent<Content.Server.Destructible.DestructibleComponent>(generator), Is.True, "an anchor's shield generator can't be destroyed.");
            }

            var core = entities.GetComponent<WFLatticeAnchorStationComponent>(stations[0]).Core;
            var blunt = new DamageSpecifier(protos.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(30000));
            damage.TryChangeDamage(core, blunt, ignoreResistances: true);

            // A station blown off the map entirely is a breach too, not only a core worn down, and so is a core deleted alone.
            entities.DeleteEntity(stations[1]);
            entities.DeleteEntity(entities.GetComponent<WFLatticeAnchorStationComponent>(stations[2]).Core);

            relay = entities.SpawnEntity("WFLatticeRelayAnchor", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(relay).NeedsPower = false;
            entities.GetComponent<WFLatticeRelayComponent>(relay).SettleSeconds = 1f;
            Assert.That(entities.GetComponent<WFLatticeRelayComponent>(relay).Fuel, Is.Zero, "a new relay came fuelled.");
            entities.GetComponent<WFLatticeRelayComponent>(relay).Fuel = 600f;
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.Integrity, Is.EqualTo(3f / 6f).Within(0.001f), "the three breaches didn't lower the Lattice's integrity.");
            Assert.That(entities.System<WFLatticeBreachObserver>().Breaches, Is.EqualTo(3), "a lost anchor wasn't announced as a breach.");
            Assert.That(entities.GetComponent<WFLatticeAnchorComponent>(relay).Active, Is.True, "the parked relay didn't take hold.");
            Assert.That(field.Anchors, Has.Count.EqualTo(4), "three primaries and the relay should hold the field.");
            var meta = entities.GetComponent<MetaDataComponent>(stations[2]).EntityName;
            Assert.That(meta, Does.StartWith("DEAD ANCHOR"), "a lost anchor's station still looks alive on radar.");
            Assert.That(entities.GetComponent<Content.Shared.Shuttles.Components.IFFComponent>(stations[2]).Color, Is.EqualTo(Color.FromHex("#6e6e6e")),
                "a lost anchor's station isn't greyed out.");

            // The standing stations' shields are up and drawn as a purple rooted Ward.
            var warded = entities.EntityQuery<ShipShieldEmitterComponent>()
                .Where(emitter => entities.HasComponent<WFLatticeAnchorShieldComponent>(emitter.Owner))
                .Count(emitter => emitter.Shield is { } shield
                                  && entities.TryGetComponent<WFWardFieldComponent>(shield, out var ward)
                                  && ward.Rooted && ward.Tint != null);
            Assert.That(warded, Is.GreaterThanOrEqualTo(5), "the anchor shields aren't up as purple Ward fields.");
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(WFLatticeEndgameCVars.AnchorFade, WFLatticeEndgameCVars.AnchorFade.DefaultValue));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The stations mapped past the Lattice's hold carry a station anchor that runs off their own power and keeps them
    /// anchored space far out, until it is destroyed.
    /// </summary>
    [TestCase("/Maps/_Mono/POI/pdvhelios.yml")]
    [TestCase("/Maps/_Mono/POI/underground.yml")]
    public async Task FarStationsHoldWithTheirMappedAnchor(string path)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var far = new System.Numerics.Vector2(45000f, 0f);

        EntityUid anchor = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            var loader = entities.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
            Assert.That(loader.TryLoadGrid(map.MapId, new Robust.Shared.Utility.ResPath(path), out var grid), Is.True);
            entities.System<SharedTransformSystem>().SetWorldPosition(grid!.Value.Owner, far);
            anchor = entities.EntityQuery<WFLatticeRelayComponent>(true)
                .Select(relay => relay.Owner)
                .Single(uid => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WFLatticeStationAnchor"
                               && entities.GetComponent<TransformComponent>(uid).GridUid == grid.Value.Owner);
        });

        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<TransformComponent>(anchor).Anchored, Is.True, $"{path}: the station anchor isn't bolted down.");
            Assert.That(entities.GetComponent<ApcPowerReceiverComponent>(anchor).Powered, Is.True, $"{path}: the station anchor has no power.");
            Assert.That(entities.GetComponent<WFLatticeAnchorComponent>(anchor).Active, Is.True, $"{path}: the station anchor didn't take hold.");
            var at = entities.System<SharedTransformSystem>().GetWorldPosition(anchor);
            Assert.That(lattice.Locate(map.MapUid, at).Band, Is.EqualTo(0), $"{path}: the station isn't anchored space with its anchor up.");
            entities.DeleteEntity(anchor);
            Assert.That(lattice.Locate(map.MapUid, at).Band, Is.GreaterThan(0), $"{path}: the station stayed anchored with its anchor gone.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Like a rooted Monolith's, an anchor's raised shield lets no ship and nobody in a suit inside it.</summary>
    [Test]
    public async Task AnchorShieldsHoldShipsAndPeopleOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var ward = entities.System<Content.Server._WF.Remade.Ward.WFWardSystem>();
        var maps = entities.System<SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();

        EntityUid station = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            station = entities.EntityQuery<WFLatticeAnchorStationComponent>().First().Owner;
        });

        await pair.RunSeconds(5f);
        EntityUid person = default, ship = default, field = default;
        System.Numerics.Vector2 centre = default, radii = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(lattice.ShieldDown(station), Is.False, "the station's shield never came up.");
            field = entities.EntityQuery<ShipShieldEmitterComponent>()
                .First(emitter => entities.HasComponent<WFLatticeAnchorShieldComponent>(emitter.Owner)
                                  && entities.GetComponent<TransformComponent>(emitter.Owner).GridUid == station).Shield!.Value;
            Assert.That(ward.TryGetOval(field, out centre, out radii), Is.True);

            // Just inside the edge on either side, off the station's deck.
            var matrix = transform.GetWorldMatrix(station);
            var personAt = System.Numerics.Vector2.Transform(centre + new System.Numerics.Vector2(radii.X * 0.9f, 0f), matrix);
            person = entities.SpawnEntity("MobHuman", new MapCoordinates(personAt, map.MapId));

            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId));
            transform.SetWorldPosition(grid.Owner, System.Numerics.Vector2.Transform(centre - new System.Numerics.Vector2(radii.X * 0.9f, 0f), matrix));
            ship = grid.Owner;
        });

        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var toLocal = transform.GetInvWorldMatrix(station);
            foreach (var (what, uid) in new[] { ("someone in a suit", person), ("a ship", ship) })
            {
                var local = System.Numerics.Vector2.Transform(transform.GetWorldPosition(uid), toLocal) - centre;
                var depth = System.MathF.Sqrt(local.X * local.X / (radii.X * radii.X) + local.Y * local.Y / (radii.Y * radii.Y));
                Assert.That(depth, Is.GreaterThanOrEqualTo(0.98f), $"{what} stayed inside a raised anchor shield.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The old alien spires inside an anchor station wake for a boarder and cut them up with flechettes.</summary>
    [Test]
    public async Task SpiresShootBoarders()
    {
        // A player among the boarders: the spires, like every NPC, sleep with nobody near.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var transform = entities.System<SharedTransformSystem>();

        var boarders = new System.Collections.Generic.List<EntityUid>();
        // No player is near, so the sleep pass would put the spires to sleep at a random point in their first seconds.
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCPauseWhenNoPlayersInRange, false));
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            var station = entities.EntityQuery<WFLatticeAnchorStationComponent>().First();
            var spires = entities.EntityQuery<MetaDataComponent>()
                .Where(meta => meta.EntityPrototype?.ID == "WFLatticeSpireTurret"
                               && entities.GetComponent<TransformComponent>(meta.Owner).GridUid == station.Owner)
                .Select(meta => meta.Owner)
                .ToList();
            Assert.That(spires, Has.Count.EqualTo(4), "the anchor station should have its four spire turrets.");

            // One boarder by each spire, so a wall in one spire's way can't decide the test.
            var core = transform.GetWorldPosition(station.Core);
            foreach (var spireUid in spires)
            {
                var spire = transform.GetWorldPosition(spireUid);
                var at = spire + System.Numerics.Vector2.Normalize(core - spire) * 2.5f;
                boarders.Add(entities.SpawnEntity("MobHuman", new MapCoordinates(at, map.MapId)));
            }

            var players = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
            players.SetAttachedEntity(players.Sessions.Single(), boarders[0], force: true);
        });

        // Turret targeting is planned off the tick, so give a slow runner a while before calling it.
        var hurt = FixedPoint2.Zero;
        for (var second = 0; second < 30 && hurt <= FixedPoint2.Zero; second++)
        {
            await pair.RunSeconds(1f);
            await server.WaitPost(() => hurt = boarders.Max(b => entities.GetComponent<DamageableComponent>(b).TotalDamage));
        }

        Assert.That(hurt, Is.GreaterThan(FixedPoint2.Zero), "the spires let every boarder walk about unhurt.");

        await pair.CleanReturnAsync();
    }

    /// <summary>A few stray hits are forgiven; past the limit the station turns its lances on the attacker.</summary>
    [Test]
    public async Task StationsAnswerAttacks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var maps = entities.System<Robust.Shared.GameObjects.SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();

        EntityUid station = default, attacker = default, crewman = default;
        await server.WaitAssertion(() =>
        {
            entities.System<WFLatticeBreachObserver>().Breaches = 0;
            // Lost anchors let go at once here, so the counts below are exact.
            server.CfgMan.SetCVar(WFLatticeEndgameCVars.AnchorFade, 0f);
            lattice.Setup(map.MapUid);
            var query = entities.EntityQueryEnumerator<WFLatticeAnchorStationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    station = uid;
            }

            var core = entities.GetComponent<WFLatticeAnchorStationComponent>(station).Core;
            Assert.That(entities.HasComponent<Content.Server.Shuttles.Components.StationAnchorComponent>(core), Is.True, "the core isn't a station anchor.");
            var lances = entities.EntityQuery<WFLatticeDefenceWeaponComponent>()
                .Count(lance => entities.GetComponent<TransformComponent>(lance.Owner).GridUid == station);
            Assert.That(lances, Is.EqualTo(8), "the loaded station should carry eight lances.");
        });

        // Let the shield come up before anyone fires on it.
        await pair.RunSeconds(5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(lattice.ShieldDown(station), Is.False, "the station's shield never came up.");
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId));
            transform.SetWorldPosition(grid.Owner, transform.GetWorldPosition(station) + new System.Numerics.Vector2(400f, 0f));
            attacker = grid.Owner;
            crewman = entities.SpawnEntity("MobHuman", new EntityCoordinates(attacker, new System.Numerics.Vector2(0.5f, 0.5f)));
            // A player aboard the attacking ship, so it counts as crewed whatever the NPCs do.
            var players = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
            players.SetAttachedEntity(players.Sessions.Single(), crewman, force: true);

            var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
            var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
            for (var i = 0; i < 9; i++)
                lattice.Strike(station, attacker, 1f);
            Assert.That(comp.NextAlert, Is.LessThanOrEqualTo(timing.CurTime), "nine hits already raised the alarm.");
            lattice.Strike(station, attacker, 1f);
            Assert.That(comp.NextAlert, Is.GreaterThan(timing.CurTime), "ten hits didn't say the station is under fire.");
        });

        // The shield holds, so the station only watches.
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(lattice.ShieldDown(station), Is.False, "the station's shield isn't up.");
            Assert.That(Bolts(entities), Is.Zero, "the lances fired while the shield held.");

            foreach (var emitter in entities.EntityQuery<ShipShieldEmitterComponent>().ToList())
            {
                if (entities.GetComponent<TransformComponent>(emitter.Owner).GridUid == station)
                    entities.DeleteEntity(emitter.Owner);
            }

            lattice.Strike(station, attacker, 1f);
        });

        // The explosive lances can wreck the one-tile attacker within seconds, which ends the fight, so watch for it, up to 30 s on a slow runner.
        var engaged = false;
        var fired = false;
        for (var i = 0; i < 120 && !(engaged && fired); i++)
        {
            await pair.RunSeconds(0.25f);
            await server.WaitPost(() =>
            {
                engaged |= entities.GetComponent<WFLatticeAnchorStationComponent>(station).Engaged;
                fired |= Bolts(entities) > 0;
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(engaged, Is.True, "the station didn't engage once its shield fell.");
            Assert.That(fired, Is.True, "the lances didn't fire with the shield down.");
            if (entities.EntityExists(crewman))
                entities.DeleteEntity(crewman);
        });

        // Nobody left alive aboard: the lances go quiet, and their bolts burn out.
        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Bolts(entities), Is.Zero, "the lances kept firing at a dead ship.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A person on foot damaging anything on a station is answered at once, shield up or not.</summary>
    [Test]
    public async Task ADirectHitProvokesTheStation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        EntityUid station = default, vandal = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            station = entities.EntityQuery<WFLatticeAnchorStationComponent>().First().Owner;
        });

        await pair.RunSeconds(5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(lattice.ShieldDown(station), Is.False, "the station's shield never came up.");
            var part = entities.EntityQuery<WFLatticeStationPartComponent>()
                .Select(p => p.Owner)
                .FirstOrDefault(uid => entities.GetComponent<TransformComponent>(uid).GridUid == station
                                       && entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID.Contains("Wall") == true);
            Assert.That(part, Is.Not.EqualTo(EntityUid.Invalid), "nothing on the station was marked.");
            var loose = entities.EntityQuery<Content.Shared._Crescent.Hardpoints.HardpointComponent>()
                .Count(h => entities.GetComponent<TransformComponent>(h.Owner).GridUid == station
                            && entities.HasComponent<Content.Shared.Construction.Components.AnchorableComponent>(h.Owner));
            Assert.That(loose, Is.Zero, "a station hardpoint can still be unwrenched.");

            vandal = entities.SpawnEntity("MobHuman", new MapCoordinates(transform.GetWorldPosition(station) + new System.Numerics.Vector2(60f, 0f), map.MapId));
            var damage = new Content.Shared.Damage.DamageSpecifier(
                server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>().Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt"), 5);
            entities.System<Content.Shared.Damage.DamageableSystem>().TryChangeDamage(part, damage, ignoreResistances: true, origin: vandal);
            Assert.That(entities.GetComponent<Content.Shared.Damage.DamageableComponent>(part).TotalDamage > 0, Is.True, "the wall took no damage.");

            var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
            Assert.That(comp.Hostiles.ContainsKey(vandal), Is.True, "the station didn't take the person on foot as its attacker.");
            Assert.That(comp.NextAlert, Is.GreaterThan(timing.CurTime), "one direct hit didn't raise the alarm.");
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(lattice.ShieldDown(station), Is.False);
            Assert.That(Bolts(entities), Is.GreaterThan(0), "the lances didn't answer a direct hit while the shield held.");
            entities.DeleteEntity(vandal);
        });

        await pair.CleanReturnAsync();
    }

    private static int Bolts(IEntityManager entities)
    {
        return entities.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WFLatticeLanceProjectile");
    }
}
