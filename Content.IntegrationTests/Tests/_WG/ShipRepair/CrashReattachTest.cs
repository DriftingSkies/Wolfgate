#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.Caverns;
using Content.Server._CE.ZLevels.Core;
using Content.Server._Mono.ShipRepair;
using Content.Server._WG.Planets.Flight;
using Content.Server._WG.Planets.Hop;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WG.ShipRepair;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WG.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WG.ShipRepair;

/// <summary>A real hull breaks up in a planet crash, and the SRD reattaches every section and rebuilds the seam.</summary>
[TestFixture]
[TestOf(typeof(WGHullSectionSystem))]
public sealed class CrashReattachTest
{
    /// <summary>Tiles of real terrain loaded around the hull before it reaches the ground.</summary>
    private const int GroundMargin = 48;

    /// <summary>A crashed and rebuilt hull, with the worker who rebuilt it.</summary>
    private sealed record Wreck(
        EntityUid Hull,
        EntityUid User,
        CavernFixture.World World,
        List<EntityUid> SwitchedOff,
        string Before,
        int Detached,
        int Rebuilt,
        List<Vector2i> Wrong,
        int Extra);

    [Test]
    public async Task CrashedHullIsReattachedAndRebuiltWhole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var wreck = await CrashAndRebuild(pair);

        TestContext.Out.WriteLine($"{wreck.Detached} sections reattached, {wreck.Rebuilt} seam tiles rebuilt, {wreck.Wrong.Count} wrong, {wreck.Extra} extra");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wreck.Rebuilt, Is.GreaterThan(0), "The crash left no seam to rebuild.");
            Assert.That(wreck.Wrong, Is.Empty, $"Hull tiles differ from the blueprint: {string.Join(", ", wreck.Wrong.Take(10))}");
            Assert.That(wreck.Extra, Is.Zero, "The hull has tiles the blueprint doesn't.");
        }

        await CavernFixture.Teardown(pair, wreck.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>A rebuilt wreck taken back to space has its running engines in its thrust banks, and its helm moves it.</summary>
    [Test]
    public async Task RebuiltWreckFliesInSpace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var wreck = await CrashAndRebuild(pair);
        var hull = wreck.Hull;

        // A thorough crew rebuilds every missing fitting too, cables under the new floor included.
        var unbuilt = await RebuildFittings(pair, wreck);

        // The engines the test switched off to force the crash go back on, as a crew would.
        await server.WaitPost(() =>
        {
            foreach (var uid in wreck.SwitchedOff)
            {
                if (em.TryGetComponent<ThrusterComponent>(uid, out var thruster) && !thruster.Enabled)
                    em.EventBus.RaiseLocalEvent(uid, new ActivateInWorldEvent(uid, uid, true));
            }
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        var report = new StringBuilder();
        report.AppendLine($"before the crash: {wreck.Before}");
        report.AppendLine($"fittings the SRD could not rebuild: {(unbuilt.Count == 0 ? "none" : string.Join(", ", unbuilt))}");
        await server.WaitPost(() => report.AppendLine($"grounded: {Describe(em, hull)}"));

        // Off the planet on the hop that leaving orbit uses.
        var space = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            space = maps.CreateMap(out _);
            var shuttle = em.GetComponent<ShuttleComponent>(hull);
            Assert.That(server.System<WGPlanetHopSystem>().TryStart((hull, shuttle), space, Vector2.Zero, null, false, out var refusal),
                Is.True, $"The rebuilt hull can't hop: {refusal}");
        });

        var arrived = false;
        for (var i = 0; i < 30 && !arrived; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() => arrived = em.GetComponent<TransformComponent>(hull).MapUid == space
                                                  && !em.HasComponent<WGPlanetHopComponent>(hull));
        }

        Assert.That(arrived, Is.True, "The rebuilt hull never came out in space.");
        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitPost(() => report.AppendLine($"in space: {Describe(em, hull)}"));
        TestContext.Out.WriteLine(report.ToString());

        var drift = new List<string>();
        var held = ShuttleButtons.None;
        await server.WaitAssertion(() =>
        {
            var shuttle = em.GetComponent<ShuttleComponent>(hull);
            var expected = new float[4];
            var members = new HashSet<EntityUid>[4];
            for (var d = 0; d < 4; d++)
            {
                members[d] = new HashSet<EntityUid>();
            }

            foreach (var uid in Children(em, hull))
            {
                if (!em.TryGetComponent<ThrusterComponent>(uid, out var thruster) || thruster.Type != ThrusterType.Linear || !thruster.IsOn)
                    continue;

                var direction = (int) em.GetComponent<TransformComponent>(uid).LocalRotation.GetCardinalDir() / 2;
                expected[direction] += thruster.Thrust;
                members[direction].Add(uid);
            }

            for (var d = 0; d < 4; d++)
            {
                if (MathF.Abs(shuttle.LinearThrust[d] - expected[d]) > 1f || shuttle.LinearThrusters[d].Count != members[d].Count || !members[d].SetEquals(shuttle.LinearThrusters[d]))
                    drift.Add($"bank {d}: {shuttle.LinearThrust[d]:F0} N from {shuttle.LinearThrusters[d].Count} engines, running engines give {expected[d]:F0} N from {members[d].Count}");
            }

            // Strafe along the bank with the most thrust; a vertical strafe reads banks 2 (up) and 0 (down).
            var best = Enumerable.Range(0, 4).MaxBy(d => shuttle.LinearThrust[d]);
            held = best switch
            {
                0 => ShuttleButtons.StrafeDown,
                1 => ShuttleButtons.StrafeRight,
                2 => ShuttleButtons.StrafeUp,
                _ => ShuttleButtons.StrafeLeft,
            };

            var body = em.GetComponent<PhysicsComponent>(hull);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(drift, Is.Empty, $"The thrust banks don't match the running engines: {string.Join("; ", drift)}\n{report}");
                Assert.That(expected.Sum(), Is.GreaterThan(0f), $"No engine runs on the rebuilt hull.\n{report}");
                Assert.That(body.BodyType, Is.EqualTo(BodyType.Dynamic), $"The rebuilt hull isn't a free body in space.\n{report}");
                Assert.That(body.BodyStatus, Is.EqualTo(BodyStatus.InAir), $"The rebuilt hull drags on the ground in space.\n{report}");
            }
        });

        // A pilot at the helm moves it.
        var start = Vector2.Zero;
        var pilot = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var console = FindShuttleConsole(em, hull);
            Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid), "The rebuilt hull has no helm.");
            pilot = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(console).Coordinates);
            em.EnsureComponent<PilotComponent>(pilot);
            server.System<ShuttleConsoleSystem>().AddPilot(console, pilot, em.GetComponent<ShuttleConsoleComponent>(console));
            var body = em.GetComponent<PhysicsComponent>(hull);
            server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(hull, Vector2.Zero, body: body);
            start = xforms.GetWorldPosition(hull);
            em.GetComponent<PilotComponent>(pilot).HeldButtons = held;
        });
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var body = em.GetComponent<PhysicsComponent>(hull);
            var moved = (xforms.GetWorldPosition(hull) - start).Length();
            Assert.That(moved, Is.GreaterThan(3f),
                $"Holding {held} for 5 s moved the rebuilt hull {moved:F1} m at {body.LinearVelocity.Length():F1} m/s.\n{report}");
            em.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.None;
        });

        await server.WaitPost(() => em.DeleteEntity(space));
        await CavernFixture.Teardown(pair, wreck.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>Crashes the vaquita on a planet with its engines off, then reattaches every section and rebuilds the seam.</summary>
    private static async Task<Wreck> CrashAndRebuild(TestPair pair)
    {
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();

        // gridSplitting off is the development preset a playtest server loads; the crash forces its own cut.
        await CavernFixture.EnableCaverns(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, false));
        var world = await CavernFixture.BuildWorld(pair, "WGSurfaceCarcinoma");
        var ground = world.Ground;
        var orbitMap = await CavernFixture.MapIdOf(pair, world.Layers[^1]);

        var hull = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(orbitMap, new ResPath("/SharedMaps/_WG/OldVessels/vaquita.yml"), out var loaded), Is.True);
            hull = loaded!.Value.Owner;
            server.System<ShipRepairSystem>().GenerateRepairData(hull);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        // Every engine off: no lift, so the touchdown is a crash.
        var switchedOff = new List<EntityUid>();
        var before = string.Empty;
        await server.WaitPost(() =>
        {
            before = Describe(em, hull);
            foreach (var uid in Children(em, hull))
            {
                if (em.TryGetComponent<ThrusterComponent>(uid, out var thruster) && thruster.Type == ThrusterType.Linear && thruster.Enabled)
                {
                    em.EventBus.RaiseLocalEvent(uid, new ActivateInWorldEvent(uid, uid, true));
                    switchedOff.Add(uid);
                }
            }
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        var refusal = await EnterAtmosphere(pair, hull, settle: 0f);
        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        var terrainLoaded = false;
        var landed = false;
        for (var step = 0; step < 150 * 6 && !landed; step++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f / 6f));

            Box2? footprint = null;
            await server.WaitPost(() =>
            {
                var map = em.GetComponent<TransformComponent>(hull).MapUid;
                landed = map == ground;

                if (!terrainLoaded && em.TryGetComponent<CEZTransitMapComponent>(map, out var transit) && transit.LowerMap == ground)
                    footprint = xforms.GetWorldMatrix(hull).TransformBox(em.GetComponent<MapGridComponent>(hull).LocalAABB);
            });

            if (footprint is not { } box)
                continue;

            terrainLoaded = true;
            await CavernFixture.LoadChunks(pair, ground,
                new Vector2i((int) MathF.Floor(box.Left) - GroundMargin, (int) MathF.Floor(box.Bottom) - GroundMargin),
                new Vector2i((int) MathF.Ceiling(box.Right) + GroundMargin, (int) MathF.Ceiling(box.Top) + GroundMargin));
        }

        Assert.That(landed, Is.True, "The hull never reached the ground.");

        // Let the wreck skid to rest.
        await server.WaitRunTicks(pair.SecondsToTicks(8f));

        var user = EntityUid.Invalid;
        var tool = EntityUid.Invalid;
        var detached = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WGCrashImpactComponent>(hull), Is.True, "The landing did not take the structural crash path.");
            Assert.That(em.HasComponent<ShipRepairDataComponent>(hull), Is.True, "The hull lost its blueprint in the crash.");
            var sections = SectionsOf(em, hull);
            detached = sections.Count;
            Assert.That(sections, Is.Not.Empty, "The crash left no sections to reattach.");
            Assert.That(sections.Any(uid => em.HasComponent<ShipRepairDataComponent>(uid)), Is.False, "A section carries its own blueprint.");

            // The worker stands on clear deck, so nothing shoves them off a do-after.
            var lattice = server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId;
            var grid = em.GetComponent<MapGridComponent>(hull);
            var floor = maps.GetAllTiles(hull, grid)
                .First(tile => tile.Tile.TypeId != lattice
                               && !maps.GetAnchoredEntities(hull, grid, tile.GridIndices)
                                   .Any(uid => em.TryGetComponent<PhysicsComponent>(uid, out var body) && body.CanCollide && body.Hard));
            var spot = new EntityCoordinates(hull, maps.TileCenterToVector((hull, grid), floor.GridIndices));
            user = em.SpawnEntity("MobHuman", spot);
            tool = em.SpawnEntity("ShipRepairDevice", spot);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            em.GetComponent<ShipRepairToolComponent>(tool).RepairTimeMultiplier = 0.02f;
        });

        // Reattach every section; one lying in another's place goes after it.
        for (var round = 0; round < 8; round++)
        {
            var sections = new List<EntityUid>();
            await server.WaitPost(() => sections = SectionsOf(em, hull));
            if (sections.Count == 0)
                break;

            foreach (var section in sections)
            {
                await server.WaitPost(() =>
                {
                    if (!em.EntityExists(section))
                        return;

                    var grid = em.GetComponent<MapGridComponent>(section);
                    var sectionSystem = server.System<WGHullSectionSystem>();
                    // Now and then a section is thrown past the SRD's reach mid-skid; a crew would tow it back first.
                    if (!sectionSystem.InReattachRange(hull, (section, grid)))
                    {
                        var home = Vector2.Transform((Vector2) sectionSystem.OffsetOf(section) * grid.TileSize, xforms.GetWorldMatrix(hull));
                        xforms.SetWorldPositionRotation(section, home, xforms.GetWorldRotation(hull));
                    }

                    var tile = maps.GetAllTiles(section, grid).First().GridIndices;
                    TestContext.Out.WriteLine($"round {round}: section {section}, " +
                                              $"{server.System<WGHullSectionSystem>().CountTiles((section, grid))} tiles, " +
                                              $"{server.System<WGHullSectionSystem>().HomeOffset(hull, (section, grid)):F1} from its place");
                    server.System<SharedChargesSystem>().AddCharges(tool, 10000);
                    Click(em, user, tool, new EntityCoordinates(section, maps.TileCenterToVector((section, grid), tile)));
                });
                await server.WaitRunTicks(pair.SecondsToTicks(1f));
            }
        }

        // Then rebuild the seam, lattice and all, from the blueprint.
        var expected = new Dictionary<Vector2i, int>();
        var rebuilt = 0;
        await server.WaitPost(() =>
        {
            Assert.That(SectionsOf(em, hull), Is.Empty, "Some sections could not be reattached.");
            em.GetComponent<ShipRepairToolComponent>(tool).RepairTimeMultiplier = 0f;
            var data = em.GetComponent<ShipRepairDataComponent>(hull);
            foreach (var (chunkIndex, chunk) in data.Chunks)
            {
                for (var i = 0; i < chunk.Tiles.Length; i++)
                {
                    if (chunk.Tiles[i] != Tile.Empty.TypeId)
                        expected[chunkIndex * data.ChunkSize + new Vector2i(i % data.ChunkSize, i / data.ChunkSize)] = chunk.Tiles[i];
                }
            }

            var grid = em.GetComponent<MapGridComponent>(hull);
            foreach (var (index, type) in expected)
            {
                if (maps.GetTileRef(hull, grid, index).Tile.TypeId == type)
                    continue;

                rebuilt++;
                server.System<SharedChargesSystem>().AddCharges(tool, 10000);
                Click(em, user, tool, new EntityCoordinates(hull, maps.TileCenterToVector((hull, grid), index)));
            }
        });
        await server.WaitRunTicks(1);

        var wrong = new List<Vector2i>();
        var extra = 0;
        await server.WaitPost(() =>
        {
            var grid = em.GetComponent<MapGridComponent>(hull);
            wrong.AddRange(expected.Where(pair => maps.GetTileRef(hull, grid, pair.Key).Tile.TypeId != pair.Value).Select(pair => pair.Key));
            extra = maps.GetAllTiles(hull, grid).Count(tile => !expected.ContainsKey(tile.GridIndices));
        });

        return new Wreck(hull, user, world, switchedOff, before, detached, rebuilt, wrong, extra);
    }

    /// <summary>Clicks the SRD at every blueprint fitting missing from the hull until none is left; returns those it couldn't rebuild.</summary>
    private static async Task<List<string>> RebuildFittings(TestPair pair, Wreck wreck)
    {
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var hull = wreck.Hull;
        var tool = EntityUid.Invalid;
        var missing = new List<(string Proto, Vector2 Position)>();

        for (var round = 0; round < 30; round++)
        {
            await server.WaitPost(() =>
            {
                tool = server.System<SharedHandsSystem>().GetActiveItem(wreck.User) ?? EntityUid.Invalid;
                missing.Clear();
                var data = em.GetComponent<ShipRepairDataComponent>(hull);
                foreach (var chunk in data.Chunks.Values)
                {
                    foreach (var spec in chunk.Entities.Values)
                    {
                        var proto = data.EntityPalette[spec.ProtoIndex];
                        if (!protos.TryIndex(proto, out var entProto) || !entProto.TryGetComponent<ShipRepairableComponent>(out _, factory))
                            continue;

                        if (spec.OriginalEntity is { } net && em.TryGetEntity(net, out var original) && !em.Deleted(original.Value)
                            && em.GetComponent<TransformComponent>(original.Value).GridUid == hull)
                            continue;

                        missing.Add((proto.Id, spec.LocalPosition));
                    }
                }

                foreach (var (_, position) in missing)
                {
                    server.System<SharedChargesSystem>().AddCharges(tool, 10000);
                    Click(em, wreck.User, tool, new EntityCoordinates(hull, position));
                }
            });

            if (missing.Count == 0)
                break;

            await server.WaitRunTicks(2);
        }

        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        return missing.Select(m => m.Proto).ToList();
    }

    /// <summary>The hull's body, thrust banks and linear engines, for a failure message.</summary>
    private static string Describe(IEntityManager em, EntityUid hull)
    {
        var body = em.GetComponent<PhysicsComponent>(hull);
        var shuttle = em.GetComponent<ShuttleComponent>(hull);
        var engines = 0;
        var enabled = 0;
        var on = 0;
        var powered = 0;
        var cooling = 0;
        var offGrid = 0;
        foreach (var uid in Children(em, hull))
        {
            if (!em.TryGetComponent<ThrusterComponent>(uid, out var thruster) || thruster.Type != ThrusterType.Linear)
                continue;

            engines++;
            enabled += thruster.Enabled ? 1 : 0;
            on += thruster.IsOn ? 1 : 0;
            powered += em.TryGetComponent<ApcPowerReceiverComponent>(uid, out var receiver) && receiver.Powered ? 1 : 0;
            cooling += em.TryGetComponent<WGAtmosphereThrusterComponent>(uid, out var state) && state.Cooling ? 1 : 0;
        }

        foreach (var bank in shuttle.LinearThrusters)
        {
            offGrid += bank.Count(uid => !em.EntityExists(uid) || em.GetComponent<TransformComponent>(uid).GridUid != hull);
        }

        var marks = string.Join(",", em.GetComponents(hull)
            .Select(comp => comp.GetType().Name)
            .Where(name => name.StartsWith("WG") || name.StartsWith("CEZ") || name.Contains("Anchor")));

        return $"{body.BodyType}/{body.BodyStatus}, mass {body.Mass:F0}, damping {body.LinearDamping:F2}, " +
               $"banks [{string.Join(", ", shuttle.LinearThrust.Select(t => t.ToString("F0")))}] " +
               $"from [{string.Join(", ", shuttle.LinearThrusters.Select(b => b.Count))}] engines ({offGrid} not on the hull), " +
               $"linear engines {engines}: {enabled} enabled, {on} on, {powered} powered, {cooling} cooling; marks {marks}\n" +
               $"  power: {DescribePower(em, hull)}";
    }

    /// <summary>Every supplier, battery and APC on the hull, and how each engine is fed.</summary>
    private static string DescribePower(IEntityManager em, EntityUid hull)
    {
        var parts = new List<string>();
        foreach (var uid in Children(em, hull))
        {
            var name = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "?";
            var anchored = em.GetComponent<TransformComponent>(uid).Anchored ? "" : " UNANCHORED";
            if (em.TryGetComponent<PowerSupplierComponent>(uid, out var supplier))
                parts.Add($"{name}{anchored} supplies {supplier.CurrentSupply:F0}/{supplier.MaxSupply:F0}{(supplier.Enabled ? "" : " off")}");

            if (em.TryGetComponent<Content.Shared.Power.Components.BatteryComponent>(uid, out var battery))
            {
                var net = em.TryGetComponent<PowerNetworkBatteryComponent>(uid, out var netBattery)
                    ? $" out {netBattery.CurrentSupply:F0}/{netBattery.MaxSupply:F0}{(netBattery.CanDischarge ? "" : " no-discharge")}"
                    : "";
                var apc = em.TryGetComponent<ApcComponent>(uid, out var apcComp)
                    ? $"{(apcComp.MainBreakerEnabled ? "" : " breaker-off")}{(em.HasComponent<WGCrashApcFaultComponent>(uid) ? " FAULT" : "")}"
                    : "";
                parts.Add($"{name}{anchored} charge {battery.CurrentCharge:F0}/{battery.MaxCharge:F0}{net}{apc}");
            }

            if (em.TryGetComponent<ThrusterComponent>(uid, out var thruster) && thruster.Type == ThrusterType.Linear
                && em.TryGetComponent<ApcPowerReceiverComponent>(uid, out var receiver))
                parts.Add($"engine {uid} load {receiver.Load:F0}{(receiver.Provider == null ? " NO-PROVIDER" : "")}{(receiver.Powered ? " powered" : " unpowered")}");
        }

        return string.Join("; ", parts);
    }

    /// <summary>Every grid linked to a hull.</summary>
    private static List<EntityUid> SectionsOf(IEntityManager entMan, EntityUid hull)
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<WGHullSectionComponent>();
        while (query.MoveNext(out var uid, out var section))
        {
            if (section.Hull == hull)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Uses the SRD at a spot, as a click within reach does.</summary>
    private static void Click(IEntityManager entMan, EntityUid user, EntityUid tool, EntityCoordinates at)
    {
        entMan.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(user, tool, null, at, true));
    }
}
