#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Contracts;
using Content.Server.Station.Systems;
using Content.Shared._WF.Contracts;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>
/// The contract board end to end, on stations built from the real Caelestinus, Sevastopol, MMC Hokkaido and Stellar
/// Blessing map configs and names: contractors post only where their station is, a courier run is done once its parcels are aboard
/// the destination, a supply crate is taken when closed full on its station and not before, a wreck's find completes a
/// recovery, lost cargo fails a contract, high tiers wait for standing, and pay that cannot reach a bank is owed.
/// </summary>
[TestFixture]
[TestOf(typeof(WFContractSystem))]
public sealed class ContractFlowTest
{
    private const string Board = "WFContractBoard";
    private const string Mob = "MobHuman";

    [Test]
    public async Task ContractsRunFromOfferToPay()
    {
        // Dirty: the test builds stations the next test should not inherit.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var contracts = entMan.System<WFContractSystem>();
        var xforms = entMan.System<SharedTransformSystem>();

        EntityUid central = default, sevastopol = default, hokkaido = default, colony = default, board = default, mob = default;
        WFContractHolder holder = default;

        await server.WaitAssertion(() =>
        {
            central = Station(server, map, Vector2.Zero, "Frontier");
            sevastopol = Station(server, map, new Vector2(4000f, 0f), "Sevastopol");
            hokkaido = Station(server, map, new Vector2(0f, 3000f), "Hokkaido");
            colony = Station(server, map, new Vector2(-600f, 0f), "TSFMCIndustry");
            board = entMan.SpawnEntity(Board, new EntityCoordinates(Largest(server, central), 4.5f, 4.5f));

            mob = entMan.SpawnEntity(Mob, new EntityCoordinates(Largest(server, central), 2.5f, 2.5f));
            var minds = entMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Test Contractor");
            minds.TransferTo(mind.Owner, mob);
            holder = contracts.HolderOf(mob)!.Value;

            Assert.That(contracts.StationOf("Sevastopol"), Is.EqualTo(sevastopol));
            Assert.That(contracts.BoardStation(), Is.EqualTo(central));
            var homes = contracts.ContractorHomes();
            Assert.That(homes.ContainsKey("WFContractorMMC") && homes.ContainsKey("WFContractorCaelPort"), Is.True);
            Assert.That(homes.ContainsKey("WFContractorCamelot"), Is.False, "Camelot posted without its station in the round.");
            Assert.That(contracts.PostOffer("WFContractCamelotParcel"), Is.Null);
        });

        // Courier: data cases from the board to Sevastopol.
        await server.WaitAssertion(() =>
        {
            var offer = contracts.PostOffer("WFContractSevDataInbound")!;
            Assert.That(offer.Destination, Is.EqualTo(sevastopol));
            Assert.That(offer.Reward, Is.GreaterThan(2000), "Pay per kilometre was not added.");
            Assert.That(contracts.TryAccept(mob, offer.Id, board), Is.Null);
            Assert.That(offer.Items, Has.Count.EqualTo(offer.Count));
            foreach (var parcel in offer.Items)
            {
                xforms.SetCoordinates(parcel, new EntityCoordinates(Largest(server, sevastopol), 1.5f, 1.5f));
            }
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder), Is.Empty, "Delivered parcels did not complete the contract.");
            Assert.That(contracts.Owed(holder), Is.GreaterThan(0), "Pay with no bank to take it should be owed.");
            Assert.That(contracts.Standing(holder, "WFContractorSevastopol"), Is.EqualTo(1));
        });

        // Supply: an iron quota for Hokkaido is not taken short, and is taken full.
        WFContract supply = default!;
        EntityUid crate = default;
        await server.WaitAssertion(() =>
        {
            supply = contracts.PostOffer("WFContractMMCIronQuota")!;
            Assert.That(contracts.TryAccept(mob, supply.Id, board), Is.Null);
            crate = supply.Items.Single();
            Fill(server, crate, "SteelOre1", supply.Goods.Single().Count - 1);
            xforms.SetCoordinates(crate, new EntityCoordinates(Largest(server, hokkaido), 1.5f, 1.5f));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder).Single().Id, Is.EqualTo(supply.Id), "A short crate was taken.");
            Assert.That(contracts.Packed(supply, crate).Single(), Is.EqualTo(supply.Goods.Single().Count - 1));
            Fill(server, crate, "SteelOre1", 1);
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder), Is.Empty, "A full crate closed on its station was not taken.");
            Assert.That(entMan.Deleted(crate) || entMan.IsQueuedForDeletion(crate), Is.True);
            Assert.That(contracts.Standing(holder, "WFContractorMMC"), Is.EqualTo(1));
        });

        // Recovery: a colony shuttle's recorder, out of a loaded wreck, back aboard the Stellar Blessing.
        await server.WaitAssertion(() =>
        {
            var recovery = contracts.PostOffer("WFContractSBLostShuttle")!;
            Assert.That(contracts.TryAccept(mob, recovery.Id, board), Is.Null);
            Assert.That(recovery.WreckGrid, Is.Not.Null);
            var recorder = recovery.Items.Single();
            Assert.That(entMan.GetComponent<TransformComponent>(recorder).GridUid, Is.EqualTo(recovery.WreckGrid));
            xforms.SetCoordinates(recorder, new EntityCoordinates(Largest(server, colony), 1.5f, 1.5f));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder), Is.Empty, "A recovered recorder did not complete the contract.");
            Assert.That(contracts.Standing(holder, "WFContractorStellarBlessing"), Is.EqualTo(1));
        });

        // Lost cargo fails, and costs the standing it earned.
        await server.WaitAssertion(() =>
        {
            var courier = contracts.PostOffer("WFContractSevDataInbound")!;
            Assert.That(contracts.TryAccept(mob, courier.Id, board), Is.Null);
            entMan.DeleteEntity(courier.Items[0]);
            Assert.That(contracts.ActiveFor(holder), Is.Empty, "Destroyed cargo did not fail the contract.");
            Assert.That(contracts.Standing(holder, "WFContractorSevastopol"), Is.EqualTo(0));
            Assert.That(contracts.State(mob).History.First().Status, Is.EqualTo(WFContractStatus.Failed));
        });

        // Tier 3 waits for standing; abandoning ends a contract and frees the slot.
        await server.WaitAssertion(() =>
        {
            var lithium = contracts.PostOffer("WFContractMMCLithium")!;
            Assert.That(contracts.Locked(holder, lithium), Is.Null, "A tier 3 contract was shut to a Junior contractor.");
            var secure = contracts.PostOffer("WFContractSevSecureTransfer")!;
            Assert.That(contracts.Locked(holder, secure), Is.Not.Null, "A tier 3 contract was open to an Applicant.");

            var parcel = contracts.PostOffer("WFContractCaelParcelRun")!;
            Assert.That(parcel.Destination, Is.Not.EqualTo(central));
            Assert.That(contracts.TryAccept(mob, parcel.Id, board), Is.Null);
            Assert.That(contracts.TryAbandon(holder, parcel.Id), Is.True);
            Assert.That(contracts.ActiveFor(holder), Is.Empty);
            Assert.That(contracts.State(mob).History.First().Status, Is.EqualTo(WFContractStatus.Abandoned));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A small station grid at a position, made from a real game map's station config.</summary>
    private static EntityUid Station(RobustIntegrationTest.ServerIntegrationInstance server, TestMapData map, Vector2 position, string gameMap)
    {
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map.MapId);
        for (var x = 0; x < 6; x++)
        {
            for (var y = 0; y < 6; y++)
            {
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
        }

        entMan.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        // Named as the round names it, which is how contractors find their stations.
        var proto = server.ProtoMan.Index<GameMapPrototype>(gameMap);
        return entMan.System<StationSystem>().InitializeNewStation(proto.Stations.Values.First(), new[] { grid.Owner }, proto.MapName);
    }

    /// <summary>Puts a number of a stacked item into a closed crate, in stacks of thirty at most.</summary>
    private static void Fill(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid crate, string single, int count)
    {
        var entMan = server.EntMan;
        var storage = entMan.System<SharedEntityStorageSystem>();
        while (count > 0)
        {
            var stack = entMan.SpawnEntity(single, entMan.GetComponent<TransformComponent>(crate).Coordinates);
            var size = System.Math.Min(30, count);
            entMan.System<SharedStackSystem>().SetCount(stack, size);
            Assert.That(storage.Insert(stack, crate), Is.True);
            count -= size;
        }
    }

    private static EntityUid Largest(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid station)
    {
        return server.EntMan.System<StationSystem>().GetLargestGrid(station)!.Value;
    }
}
