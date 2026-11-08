#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.PlanetMap;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>An outpost's landing pad takes a ship with access down from orbit onto its square and back up.</summary>
[TestFixture]
[TestOf(typeof(WFLandingPadSystem))]
public sealed class OutpostPadTest
{
    /// <summary>The pad's tile on the founded outpost, clear of its console.</summary>
    private static readonly Vector2i PadTile = new(1, -5);

    /// <summary>
    /// A lander short of lift over the outpost's pad is refused while the pad is too small, then guided down once it is
    /// wide enough: it rests squared to the outpost on the pad, static, with the outpost and a crowbar on its floor
    /// untouched. Lifting off sends it back to orbit and frees the pad.
    /// </summary>
    [Test]
    public async Task AHullShortOfLiftLandsOnThePadAndLaunches()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var outposts = server.System<WFOutpostSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        var entry = server.System<WFOrbitEntrySystem>();

        try
        {
            EntityUid outpost = default;
            EntityUid pad = default;
            EntityUid crowbar = default;
            Vector2 padWorld = default;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpost = found!.Value;
                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                // A strip of plates south from the founding floor out to the pad, and one beside it.
                for (var y = -1; y >= PadTile.Y; y--)
                    Extend(new Vector2i(PadTile.X, y));
                Extend(PadTile + new Vector2i(1, 2));

                void Extend(Vector2i tile)
                {
                    var at = transform.ToMapCoordinates(new EntityCoordinates(outpost, tile + new Vector2(0.5f)));
                    Assert.That(outposts.TryExtend(user, "Plating", transform.ToCoordinates(ground, at), out var why), Is.True,
                        $"The plate at {tile} was refused: {why}.");
                }

                pad = entMan.SpawnEntity("WFOutpostLandingPad", maps.GridTileToLocal(outpost, grid, PadTile));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(pad, false);
                entMan.GetComponent<WFLandingPadComponent>(pad).Size = 5;
                crowbar = entMan.SpawnEntity("Crowbar", maps.GridTileToLocal(outpost, grid, PadTile + new Vector2i(1, 2)));
                padWorld = transform.GetWorldPosition(pad);
            });

            // A lander short of lift with its landing thrusters pulled, in orbit over the pad.
            var hull = await BuildLander(pair, entMan.GetComponent<MapComponent>(orbit).MapId, padWorld - new Vector2(3.5f, 4.5f) + new Vector2(6f, 3f));
            var console = EntityUid.Invalid;
            await server.WaitPost(() =>
            {
                foreach (var thruster in OnGrid(entMan, hull).Where(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WFThrusterLanding"))
                    entMan.DeleteEntity(thruster);
                console = FindShuttleConsole(entMan, hull);
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                Assert.That(entry.TryEnterAtmosphere(console, false, out var reason, user), Is.False, "A 5x5 pad took a 7x9 hull.");
                Assert.That(reason, Does.Contain("5×5"), "The refusal gives the pad's size.");
                entMan.GetComponent<WFLandingPadComponent>(pad).Size = 9;
                Assert.That(entry.TryEnterAtmosphere(console, false, out reason, user), Is.True, $"The pad refused the hull: {reason}.");
                Assert.That(entMan.GetComponent<WFLandingPadComponent>(pad).Guiding, Is.True);
            });

            await pair.RunTicksSync(pair.SecondsToTicks(12f));

            Vector2 rest = default;
            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(hull);
                Assert.That(xform.MapUid, Is.EqualTo(ground), "The hull never came down.");
                Assert.That(entMan.GetComponent<WFPadDockedComponent>(hull).Pad, Is.EqualTo(pad));
                Assert.That(entMan.GetComponent<WFLandingPadComponent>(pad).Parked, Is.EqualTo(hull));
                Assert.That(entMan.GetComponent<WFLandingPadComponent>(pad).Guiding, Is.False);
                Assert.That(entMan.GetComponent<PhysicsComponent>(hull).BodyType, Is.EqualTo(BodyType.Static), "A parked hull is static.");
                Assert.That((transform.GetWorldRotation(hull) - transform.GetWorldRotation(outpost)).Reduced().Theta, Is.EqualTo(0).Within(0.01),
                    "The hull is squared to the outpost.");

                // Snapped to the outpost's tiles, its middle on the pad's.
                var local = Vector2.Transform(transform.GetWorldPosition(hull), transform.GetInvWorldMatrix(outpost));
                Assert.That(local.X - MathF.Round(local.X), Is.EqualTo(0).Within(0.01));
                Assert.That(local.Y - MathF.Round(local.Y), Is.EqualTo(0).Within(0.01));
                var middle = Vector2.Transform(entMan.GetComponent<MapGridComponent>(hull).LocalAABB.Center, transform.GetWorldMatrix(hull));
                Assert.That(Vector2.Distance(middle, padWorld), Is.LessThan(0.75f), "The hull's middle is on the pad.");

                Assert.That(entMan.EntityExists(outpost) && entMan.EntityExists(entMan.GetComponent<WFOutpostComponent>(outpost).Console), Is.True,
                    "The outpost and its console stand.");
                Assert.That(entMan.EntityExists(pad) && entMan.EntityExists(crowbar), Is.True, "Touchdown crushed what stands on the pad.");
                rest = transform.GetWorldPosition(hull);
            });

            await pair.RunTicksSync(pair.SecondsToTicks(2f));
            await server.WaitAssertion(() =>
                Assert.That(Vector2.Distance(transform.GetWorldPosition(hull), rest), Is.LessThan(0.01f), "The parked hull moved."));

            var pilot = await HoldVertical(pair, hull, ShuttleButtons.None);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<CEZLevelsSystem>().WfTryBeginLiftoff(hull, console, pilot, out var reason), Is.True,
                    $"The pad refused to launch the hull: {reason}.");
                Assert.That(entMan.GetComponent<WFLandingPadComponent>(pad).Guiding, Is.True);
            });

            await pair.RunTicksSync(pair.SecondsToTicks(12f));

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit), "The hull never reached orbit.");
                Assert.That(entMan.HasComponent<WFPadDockedComponent>(hull), Is.False);
                Assert.That(entMan.GetComponent<WFLandingPadComponent>(pad).Parked, Is.Null, "The pad is free again.");
                Assert.That(entMan.GetComponent<WFLandingPadComponent>(pad).Guiding, Is.False);
                Assert.That(entMan.GetComponent<PhysicsComponent>(hull).BodyType, Is.EqualTo(BodyType.Dynamic), "The hull flies again.");
                Assert.That(entMan.EntityExists(outpost), Is.True);
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A Tradepost's public pad on open ground takes a pilot with no access to anything and shows on the orbit radar; its
    /// public hub takes the drops of an outpost whose own hub has no power.
    /// </summary>
    [Test]
    public async Task APublicPadTakesAnyPilotAndAPublicHubStandsIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var outposts = server.System<WFOutpostSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        var entry = server.System<WFOrbitEntrySystem>();
        var padTile = new Vector2i(27, 0);
        await LayTiles(pair, ground, padTile - new Vector2i(8, 8), padTile + new Vector2i(8, 8));

        try
        {
            EntityUid pad = default;
            EntityUid stranger = default;
            EntityUid outpostUid = default;
            EntityUid ownHub = default;
            EntityUid sharedHub = default;
            Vector2 padWorld = default;
            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                // An apron clear of the test world's rock, as a Tradepost's is.
                for (var x = -8; x <= 8; x++)
                for (var y = -8; y <= 8; y++)
                {
                    foreach (var rock in maps.GetAnchoredEntities(ground, grid, padTile + new Vector2i(x, y)).ToList())
                        entMan.DeleteEntity(rock);
                }

                pad = entMan.SpawnEntity("WFPoiPublicLandingPad", maps.GridTileToLocal(ground, grid, padTile));
                Assert.That(entMan.GetComponent<TransformComponent>(pad).Anchored, Is.True);
                padWorld = transform.GetWorldPosition(pad);
                stranger = entMan.SpawnEntity(null, new EntityCoordinates(ground, Vector2.Zero));

                // An outpost whose own hub has no power sends its drops to the public hub on its world.
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = (found!.Value, entMan.GetComponent<WFOutpostComponent>(found.Value));
                Assert.That(outposts.GetHubStatus(outpost, out _), Is.EqualTo(WFOutpostHubStatus.None));
                var at = transform.ToMapCoordinates(new EntityCoordinates(found.Value, new Vector2(1.5f, -0.5f)));
                Assert.That(outposts.TryExtend(user, "Plating", transform.ToCoordinates(ground, at), out var why), Is.True, why);
                var ownGrid = entMan.GetComponent<MapGridComponent>(found.Value);
                var own = entMan.SpawnEntity("WFOutpostTradeHub", maps.GridTileToLocal(found.Value, ownGrid, new Vector2i(1, -1)));
                Assert.That(outposts.GetHubStatus(outpost, out var hub), Is.Not.EqualTo(WFOutpostHubStatus.Ready));
                Assert.That(hub, Is.EqualTo(own));

                ownHub = own;
                outpostUid = found.Value;
                sharedHub = entMan.SpawnEntity("WFPoiPublicTradeHub", maps.GridTileToLocal(ground, grid, padTile + new Vector2i(-7, 7)));
            });

            // Power settles on the power net's next update.
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            await server.WaitAssertion(() =>
            {
                var outpost = (outpostUid, entMan.GetComponent<WFOutpostComponent>(outpostUid));
                Assert.That(outposts.GetHubStatus(outpost, out var hub), Is.EqualTo(WFOutpostHubStatus.Ready));
                Assert.That(hub, Is.EqualTo(sharedHub), "The public hub stands in for the unpowered one.");
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(ownHub, false);
            });

            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            await server.WaitAssertion(() =>
            {
                var outpost = (outpostUid, entMan.GetComponent<WFOutpostComponent>(outpostUid));
                Assert.That(outposts.GetHubStatus(outpost, out var hub), Is.EqualTo(WFOutpostHubStatus.Ready));
                Assert.That(hub, Is.EqualTo(ownHub), "An outpost's own ready hub comes first.");
            });

            var hull = await BuildLander(pair, entMan.GetComponent<MapComponent>(orbit).MapId, padWorld - new Vector2(3.5f, 4.5f) + new Vector2(6f, 3f));
            var console = EntityUid.Invalid;
            await server.WaitPost(() =>
            {
                foreach (var thruster in OnGrid(entMan, hull).Where(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WFThrusterLanding"))
                    entMan.DeleteEntity(thruster);
                console = FindShuttleConsole(entMan, hull);
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
                Assert.That(entry.TryEnterAtmosphere(console, false, out var reason, stranger), Is.True, $"The public pad refused a stranger: {reason}."));

            await pair.RunTicksSync(pair.SecondsToTicks(12f));

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground), "The hull never came down.");
                Assert.That(entMan.GetComponent<WFLandingPadComponent>(pad).Parked, Is.EqualTo(hull));
                var middle = Vector2.Transform(entMan.GetComponent<MapGridComponent>(hull).LocalAABB.Center, transform.GetWorldMatrix(hull));
                Assert.That(Vector2.Distance(middle, padWorld), Is.LessThan(0.75f), "The hull's middle is on the pad.");

                var radar = entMan.EnsureComponent<WFPlanetMapComponent>(orbit);
                server.System<WFLandingPadSystem>().UpdateRadar();
                Assert.That(radar.Pads, Has.Count.EqualTo(1), "The orbit radar lists the world's pad.");
                Assert.That(radar.Pads[0].Public, Is.True);
                Assert.That(radar.Pads[0].Size, Is.EqualTo(13));
                Assert.That(Vector2.Distance(radar.Pads[0].Centre, padWorld), Is.LessThan(0.01f));
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static List<EntityUid> OnGrid(IEntityManager entMan, EntityUid grid)
    {
        var found = new List<EntityUid>();
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
            found.Add(child);
        return found;
    }
}
