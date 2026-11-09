using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Sector;
using Content.Shared._WF.Remade.Sector;
using Robust.Shared.Prototypes;
using Content.Shared._WF.DroneRings;
using Content.Shared._WF.Lattice;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>A sector map gets its primary anchors, bands fall by distance, and a lost anchor moves them.</summary>
public sealed class WFLatticeTest
{
    [Test]
    public async Task SetupSpawnsAnchorsAndBandsShiftWhenOneIsLost()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var transform = entities.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            // The shift is checked at once here; LostAnchorsFadeSlowly covers the fade.
            server.CfgMan.SetCVar(WFLatticeEndgameCVars.AnchorFade, 0f);
            lattice.Setup(map.MapUid);

            var anchors = new List<EntityUid>();
            var query = entities.EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    anchors.Add(uid);
            }

            Assert.That(anchors, Has.Count.EqualTo(6));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(15000f, 0f)), Is.EqualTo(0));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(0f, 27000f)), Is.EqualTo(1));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(-42000f, 0f)), Is.EqualTo(2));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(0f, -58000f)), Is.EqualTo(3));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(72000f, 0f)), Is.EqualTo(4));

            // Setting up again keeps the anchors it has.
            lattice.Setup(map.MapUid);
            var count = 0;
            var again = entities.EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
            while (again.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    count++;
            }

            Assert.That(count, Is.EqualTo(6));

            var lost = anchors[0];
            var direction = Vector2.Normalize(transform.GetWorldPosition(lost));
            Assert.That(lattice.BandAt(map.MapUid, direction * 18200f), Is.EqualTo(0));
            entities.DeleteEntity(lost);
            Assert.That(lattice.BandAt(map.MapUid, direction * 18200f), Is.EqualTo(1));
            Assert.That(lattice.BandAt(map.MapUid, -direction * 18200f), Is.EqualTo(0));
        });

        // Clients get the anchors that are left once the field next ticks.
        await server.WaitRunTicks(90);
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.Anchors, Has.Count.EqualTo(5));
            Assert.That(field.Strengths, Has.Count.EqualTo(5));
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(WFLatticeEndgameCVars.AnchorFade, WFLatticeEndgameCVars.AnchorFade.DefaultValue));
        await pair.CleanReturnAsync();
    }

    /// <summary>The bands follow a map's drone rings when there is one per edge, and the Deep garbles.</summary>
    /// <summary>An unwatched wreck in the Deep can fill with Remade and show on radar as a lost ship.</summary>
    [Test]
    public async Task AWreckInTheDeepBecomesALostShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var maps = entities.System<SharedMapSystem>();

        EntityUid wreck = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            wreck = entities.SpawnEntity("NFWreckDebrisSmall", new Robust.Shared.Map.MapCoordinates(new Vector2(72000f, 0f), map.MapId));
            var grid = entities.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(wreck);
            var plating = new Robust.Shared.Map.Tile(server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId);
            for (var x = 0; x < 6; x++)
            {
                for (var y = 0; y < 6; y++)
                {
                    maps.SetTile(wreck, grid, new Robust.Shared.Maths.Vector2i(x, y), plating);
                }
            }
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(lattice.TryLoseShip((map.MapUid, field), protos.Index<WFLatticeProfilePrototype>(field.Profile)), Is.True, "no wreck in the Deep turned.");
            Assert.That(entities.HasComponent<WFLatticeLostShipComponent>(wreck), Is.True);
            var mobs = protos.Index<WFLatticeProfilePrototype>(field.Profile).LostShipMobs.Select(id => id.Id).ToHashSet();
            var remade = entities.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype is { } proto && mobs.Contains(proto.ID)
                               && entities.GetComponent<TransformComponent>(meta.Owner).GridUid == wreck);
            Assert.That(remade, Is.InRange(2, 4), "the lost ship holds the wrong number of Remade.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The bands keep the profile's edges whatever a map's drone rings say, and are near-circles round the centre: a
    /// relay anchor makes only a well of anchored space round itself, not a bulge in the Coda's edge. The Deep garbles.
    /// </summary>
    [Test]
    public async Task BandsKeepTheirEdgesAndTheDeepGarbles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();

        await server.WaitAssertion(() =>
        {
            entities.EnsureComponent<WFDroneRingsComponent>(map.MapUid).Radii = new List<float> { 10000f, 12000f, 14000f, 16000f };
            lattice.Setup(map.MapUid);

            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.Edges, Is.EqualTo(new List<float> { 0f, 20000f, 34000f, 50000f, 66000f }), "drone rings moved the band edges.");

            // Every direction crosses into the Coda within a kilometre of its edge.
            for (var angle = 0f; angle < MathF.Tau; angle += 0.3f)
            {
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Assert.That(lattice.Locate(map.MapUid, direction * 65000f).Band, Is.EqualTo(3), $"not Feedback just inside the Coda at {angle} rad.");
                Assert.That(lattice.Locate(map.MapUid, direction * 67000f).Band, Is.EqualTo(4), $"not the Coda just past its edge at {angle} rad.");
            }

            // A relay out in Dissonance anchors the space round it and leaves the Coda's edge where it was.
            var relay = new Vector2(0f, 46000f);
            Assert.That(lattice.Locate(map.MapUid, relay).Band, Is.EqualTo(2));
            entities.SpawnEntity("WFLatticeAnchorRelayMarker", new Robust.Shared.Map.MapCoordinates(relay, map.MapId));
            Assert.That(lattice.Locate(map.MapUid, relay + new Vector2(500f, 0f)).Band, Is.EqualTo(0), "the relay held no anchored space round it.");
            Assert.That(lattice.Locate(map.MapUid, new Vector2(0f, 67000f)).Band, Is.EqualTo(4), "the relay pushed the Coda's edge out.");

            const string text = "12345 67890";
            Assert.That(lattice.GarbleAt(text, map.MapUid, new Vector2(5000f, 0f), 1), Is.EqualTo(text));
            Assert.That(lattice.GarbleAt(text, map.MapUid, new Vector2(72000f, 0f), 1), Is.Not.EqualTo(text));

            var centres = new List<Robust.Shared.Map.MapCoordinates>();
            lattice.GetCentres(centres);
            Assert.That(centres, Does.Contain(new Robust.Shared.Map.MapCoordinates(Vector2.Zero, map.MapId)));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A Monolith hulk is only ever placed in the Deep.</summary>
    [Test]
    public async Task MonolithHulksArePlacedInTheDeep()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var sites = entities.System<WFRemadeSiteSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            Assert.That(sites.TryStartSite(protos.Index<WFRemadeSitePrototype>("WFRemadeSiteHulk"), null, false, out var encounter), Is.True);

            var origin = entities.GetComponent<WFEncounterComponent>(encounter).Origin;
            Assert.That(origin.MapId, Is.EqualTo(map.MapId));
            Assert.That(lattice.Locate(map.MapUid, origin.Position).Band, Is.EqualTo(4));
        });

        await pair.CleanReturnAsync();
    }
}
