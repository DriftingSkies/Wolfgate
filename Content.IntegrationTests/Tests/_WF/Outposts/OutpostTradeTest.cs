#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Power.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.Planets.Parachute;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// Outpost trade: an order from the Trade tab is paid for, comes down by parachute from the sky onto the trade hub, or
/// next to it once the hub is blocked, and no more than five are on their way at once. Every listing, kit and add-on
/// costs at least what it appraises at once delivered and deployed.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostTradeTest
{
    private const string Crate = "WFOutpostListingSteel";
    private const string Pack = "WFOutpostListingFabricator";

    [Test]
    public async Task OrdersLandOnTheHub()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var bank = server.System<BankSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        var protos = server.ProtoMan;

        EntityUid outpost = default;
        EntityUid hub = default;
        var balance = 0;

        try
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostCVars.TradeLeadTime, 1f));
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpost = found!.Value;
                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                var comp = entMan.GetComponent<WFOutpostComponent>(outpost);
                var console = (comp.Console, entMan.GetComponent<WFOutpostConsoleComponent>(comp.Console));

                Assert.That(outposts.TryOrder(user, console, (outpost, comp), Crate, out reason), Is.False, "No hub, no order.");
                Assert.That(reason, Is.EqualTo("wf-outpost-trade-no-hub"));

                // From the corner tile, the hub goes east of it, beside the console.
                transform.SetCoordinates(user, maps.GridTileToLocal(outpost, grid, new Vector2i(0, 0)));
                transform.SetWorldRotation(user, Direction.East.ToAngle());
                var pack = entMan.SpawnEntity("WFOutpostTradeHubPack", entMan.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(outposts.TryDeploy(user, (pack, entMan.GetComponent<WFDeployableComponent>(pack)), out var deployed, out reason), Is.True,
                    $"The hub wasn't deployed: {reason}.");
                hub = deployed!.Value;

                Assert.That(outposts.GetHubStatus((outpost, comp), out _), Is.EqualTo(WFOutpostHubStatus.Unpowered));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(hub, false);
                entMan.GetComponent<ApcPowerReceiverComponent>(hub).Powered = true;
                Assert.That(outposts.GetHubStatus((outpost, comp), out _), Is.EqualTo(WFOutpostHubStatus.Ready));

                entMan.EnsureComponent<BankAccountComponent>(user);
                Assert.That(bank.TryBankDeposit(user, 50_000, tax: false), Is.True, "Could not fund the owner.");
                Assert.That(bank.TryGetBalance(user, out balance), Is.True);

                // A crate first, so the hub's tile is taken for the next, then packs up to the limit.
                Assert.That(outposts.TryOrder(user, console, (outpost, comp), Crate, out reason), Is.True, $"Order refused: {reason}.");
                for (var i = 1; i < WFOutpostLimits.OrdersInFlight; i++)
                {
                    Assert.That(outposts.TryOrder(user, console, (outpost, comp), Pack, out reason), Is.True, $"Order refused: {reason}.");
                }

                Assert.That(outposts.TryOrder(user, console, (outpost, comp), Pack, out reason), Is.False, "A sixth order in flight is refused.");
                Assert.That(reason, Is.EqualTo("wf-outpost-trade-full"));

                var cost = protos.Index<CargoProductPrototype>(Crate).Cost
                           + (WFOutpostLimits.OrdersInFlight - 1) * protos.Index<CargoProductPrototype>(Pack).Cost;
                Assert.That(bank.TryGetBalance(user, out var after) && after == balance - cost, Is.True, "The owner paid for each order.");
                balance = after;
            });

            await WaitForOrders(pair, outpost);

            EntityUid first = default;
            await server.WaitAssertion(() =>
            {
                var landed = Landed(entMan, ground, "CrateMaterialSteel");
                Assert.That(landed, Has.Count.EqualTo(1), "The crate came down on the ground.");
                first = landed[0];
                Assert.That(Distance(entMan, transform, first, hub), Is.LessThan(0.5f), "The first drop lands on the hub.");

                var packs = Landed(entMan, ground, "WFOutpostFabricatorPack");
                Assert.That(packs, Has.Count.EqualTo(WFOutpostLimits.OrdersInFlight - 1), "Every pack came down.");
                foreach (var pack in packs)
                {
                    Assert.That(Distance(entMan, transform, pack, hub), Is.LessThanOrEqualTo(5.5f), "A pack lands within 5 tiles of the hub.");
                }

                var comp = entMan.GetComponent<WFOutpostComponent>(outpost);
                var console = (comp.Console, entMan.GetComponent<WFOutpostConsoleComponent>(comp.Console));
                Assert.That(outposts.TryOrder(user, console, (outpost, comp), Crate, out var reason), Is.True, $"Order refused: {reason}.");
            });

            await WaitForOrders(pair, outpost);

            await server.WaitAssertion(() =>
            {
                var second = Landed(entMan, ground, "CrateMaterialSteel").Single(crate => crate != first);
                var distance = Distance(entMan, transform, second, hub);
                Assert.That(distance, Is.GreaterThan(0.5f), "The hub is blocked by the first crate, so the next lands beside it.");
                Assert.That(distance, Is.LessThanOrEqualTo(5.5f), "It still lands within 5 tiles of the hub.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every Trade listing costs at least what its delivery appraises at, and every outfitter item, the Planet Outpost Kit
    /// among them, sells for at least what it appraises at with every pack in it deployed.
    /// </summary>
    [Test]
    public async Task PricesCoverAppraisal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var pricing = server.System<PricingSystem>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var listings = protos.EnumeratePrototypes<CargoProductPrototype>().Where(p => p.Group == WFOutpostLimits.TradeGroup).ToList();
            Assert.That(listings, Is.Not.Empty);

            var outfitter = protos.Index<VendingMachineInventoryPrototype>("WFOutpostOutfitterInventory");
            Assert.That(outfitter.StartingInventory, Does.ContainKey("WFCratePlanetOutpostKit"));

            Assert.Multiple(() =>
            {
                foreach (var listing in listings)
                {
                    var appraisal = Appraise(listing.Product);
                    Assert.That(listing.Cost, Is.GreaterThanOrEqualTo(appraisal), $"{listing.ID} costs less than its delivery appraises at.");
                }

                foreach (var id in outfitter.StartingInventory.Keys)
                {
                    var proto = protos.Index(id);
                    var price = pricing.GetEstimatedVendPrice(proto);
                    if (price <= 0)
                        price = pricing.GetEstimatedPrice(proto);

                    // Set up, each pack it holds (or it is) is worth the machine it deploys instead.
                    var deployed = Appraise(id);
                    foreach (var (content, amount) in Contents(proto).Append((id, 1)))
                    {
                        if (!protos.Index(content).TryGetComponent<WFDeployableComponent>(out var deployable, factory))
                            continue;

                        var gain = pricing.GetEstimatedPrice(protos.Index(deployable.Machine)) - pricing.GetEstimatedPrice(protos.Index(content));
                        deployed += amount * Math.Max(0, gain);
                    }

                    Assert.That(price, Is.GreaterThanOrEqualTo(deployed), $"{id} sells for less than it appraises at, deployed.");
                }
            });

            double Appraise(string id)
            {
                var ent = entMan.SpawnEntity(id, map.GridCoords);
                var price = pricing.GetPrice(ent);
                entMan.DeleteEntity(ent);
                return price;
            }

            IEnumerable<(string Id, int Amount)> Contents(EntityPrototype proto)
            {
                if (!proto.TryGetComponent<StorageFillComponent>(out var fill, factory))
                    return Enumerable.Empty<(string, int)>();

                return fill.Contents.Where(entry => entry.PrototypeId != null).Select(entry => (entry.PrototypeId!.Value.Id, entry.Amount));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Runs the server until every order of the outpost has landed, failing after a minute of game time.</summary>
    private static async Task WaitForOrders(TestPair pair, EntityUid outpost)
    {
        var entMan = pair.Server.EntMan;
        for (var i = 0; i < 60; i++)
        {
            await pair.RunTicksSync(30);
            var done = false;
            await pair.Server.WaitPost(() => done = entMan.GetComponent<WFOutpostComponent>(outpost).Orders.Count == 0);
            if (done)
                return;
        }

        var report = "";
        await pair.Server.WaitPost(() =>
        {
            foreach (var order in entMan.GetComponent<WFOutpostComponent>(outpost).Orders)
            {
                report += $" | {order.Product} drops {order.Drops} delivery {order.Delivery}";
                if (order.Delivery is not { } d || !entMan.EntityExists(d))
                    continue;
                var xform = entMan.GetComponent<TransformComponent>(d);
                entMan.TryGetComponent<CEZPhysicsComponent>(d, out var z);
                entMan.TryGetComponent<WFParachutedComponent>(d, out var chute);
                report += $" map {xform.MapUid} parent {xform.ParentUid} pos {xform.WorldPosition} z {z?.LocalPosition} v {z?.Velocity} ground {z?.CachedGroundHeight} fallable {z?.Fallable} chute {chute?.Deployed}";
            }
        });
        Assert.Fail($"The outpost's orders never landed:{report}");
    }

    /// <summary>Everything of a prototype on the ground map, not still in the air above it.</summary>
    private static List<EntityUid> Landed(IEntityManager entMan, EntityUid ground, string proto)
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == proto && xform.MapUid == ground)
                found.Add(uid);
        }

        return found;
    }

    private static float Distance(IEntityManager entMan, SharedTransformSystem transform, EntityUid a, EntityUid b)
    {
        return Vector2.Distance(transform.GetWorldPosition(a), transform.GetWorldPosition(b));
    }
}
