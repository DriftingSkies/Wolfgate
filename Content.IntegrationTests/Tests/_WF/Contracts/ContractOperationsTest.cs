#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Contracts;
using Content.Shared._WF.Contracts;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>Mail runs, sweeps, clearing, repairs, downloads and twin surveys end to end; chain links stay off the board.</summary>
[TestFixture]
[TestOf(typeof(WFContractSystem))]
public sealed class ContractOperationsTest
{
    [Test]
    public async Task NewObjectivesRun()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var contracts = entMan.System<WFContractSystem>();
        var xforms = entMan.System<SharedTransformSystem>();

        EntityUid central = default, sevastopol = default, hokkaido = default, colony = default, lab = default, board = default, mob = default;
        WFContractHolder holder = default;

        await server.WaitAssertion(() =>
        {
            central = ContractFlowTest.Station(server, map, Vector2.Zero, "Frontier");
            sevastopol = ContractFlowTest.Station(server, map, new Vector2(4000f, 0f), "Sevastopol");
            hokkaido = ContractFlowTest.Station(server, map, new Vector2(0f, 3000f), "Hokkaido");
            colony = ContractFlowTest.Station(server, map, new Vector2(-600f, 0f), "TSFMCIndustry");
            lab = ContractFlowTest.Station(server, map, new Vector2(0f, -4000f), "AnomalousLab");
            board = entMan.SpawnEntity("WFContractBoard", new EntityCoordinates(ContractFlowTest.Largest(server, central), 4.5f, 4.5f));
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
            var minds = entMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Test Operator");
            minds.TransferTo(mind.Owner, mob);
            holder = contracts.HolderOf(mob)!.Value;
        });

        // Mail run: one parcel per stop, each counted only at its own station.
        WFContract mail = default!;
        await server.WaitAssertion(() =>
        {
            mail = Take(contracts, mob, board, "WFContractCaelMailSweep");
            Assert.That(mail.Stops, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(mail.Stops, Does.Not.Contain(central), "A mail stop was the board's own station.");
            Assert.That(mail.ParcelStops.Values, Is.EquivalentTo(mail.Stops));
            // A parcel at another stop's station must not count.
            var (first, stop) = mail.ParcelStops.First();
            var wrong = mail.Stops.First(s => s != stop);
            xforms.SetCoordinates(first, new EntityCoordinates(ContractFlowTest.Largest(server, wrong), 1.5f, 1.5f));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(mail.Delivered, Is.Zero, "A parcel counted at another stop's station.");
            foreach (var (parcel, stop) in mail.ParcelStops)
            {
                xforms.SetCoordinates(parcel, new EntityCoordinates(ContractFlowTest.Largest(server, stop), 1.5f, 1.5f));
            }
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() => Assert.That(contracts.ActiveFor(holder), Is.Empty, "Delivering every parcel did not complete the run."));

        // Sweep: each point is logged as the holder flies past it.
        WFContract sweep = default!;
        await server.WaitAssertion(() =>
        {
            sweep = Take(contracts, mob, board, "WFContractCaelLaneSurvey");
            Assert.That(sweep.Points, Has.Count.EqualTo(sweep.Count));
        });
        for (var i = 0; i < sweep.Points.Count; i++)
        {
            var point = sweep.Points[i];
            await server.WaitAssertion(() => xforms.SetMapCoordinates(mob, new MapCoordinates(point.Position + new Vector2(20f, 0f), point.MapId)));
            await pair.RunTicksSync(90);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder), Is.Empty, "Passing every point did not complete the sweep.");
            xforms.SetCoordinates(mob, new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
        });

        // Clearing: done once every nest is gone.
        WFContract clear = default!;
        await server.WaitAssertion(() =>
        {
            clear = Take(contracts, mob, board, "WFContractMarshalsCarpNests");
            Assert.That(clear.Targets, Has.Count.GreaterThan(0));
            Assert.That(clear.Targets.All(t => entMan.GetComponent<TransformComponent>(t).GridUid == clear.WreckGrid), Is.True);
            foreach (var nest in clear.Targets)
            {
                entMan.DeleteEntity(nest);
            }
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() => Assert.That(contracts.ActiveFor(holder), Is.Empty, "Destroying every nest did not complete the clearing."));

        // Repair: welded whole is not enough without a cell, and the cell alone is not enough while damaged.
        WFContract repair = default!;
        EntityUid beacon = default;
        await server.WaitAssertion(() =>
        {
            repair = Take(contracts, mob, board, "WFContractCaelBeacon");
            beacon = repair.Targets.Single();
            Assert.That(entMan.GetComponent<DamageableComponent>(beacon).TotalDamage > 0, Is.True, "The beacon started whole.");
            var cell = entMan.SpawnEntity("PowerCellSmall", entMan.GetComponent<TransformComponent>(beacon).Coordinates);
            Assert.That(entMan.System<ItemSlotsSystem>().TryInsert(beacon, "cell_slot", cell, null), Is.True);
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(contracts.ActiveFor(holder).Single().Id, Is.EqualTo(repair.Id), "A damaged beacon with a cell counted as repaired.");
            entMan.System<DamageableSystem>().SetAllDamage(beacon, entMan.GetComponent<DamageableComponent>(beacon), 0);
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() => Assert.That(contracts.ActiveFor(holder), Is.Empty, "A whole, powered beacon did not complete the repair."));

        // Download: no progress while no one stands by, progress once someone does.
        WFContract download = default!;
        await server.WaitAssertion(() =>
        {
            download = Take(contracts, mob, board, "WFContractSevArchivePull");
            Assert.That(download.Targets, Has.Count.EqualTo(1));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(download.Started, Is.False, "The download ran with no one beside the terminal.");
            var terminal = entMan.GetComponent<TransformComponent>(download.Targets[0]).Coordinates;
            xforms.SetCoordinates(mob, terminal.Offset(new Vector2(1f, 0f)));
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(download.Started && download.Progress > System.TimeSpan.Zero, Is.True, "Standing by the terminal did not run the download.");
            Assert.That(contracts.TryAbandon(holder, download.Id), Is.True);
            xforms.SetCoordinates(mob, new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
        });

        // Freight crates each take their own tile; parcels would share one.
        await server.WaitAssertion(() =>
        {
            var freight = Take(contracts, mob, board, "WFContractMMCMachineParts", crewCheck: false);
            var tiles = freight.Items.Select(i => entMan.GetComponent<TransformComponent>(i).Coordinates.Position).Select(p => (System.MathF.Floor(p.X), System.MathF.Floor(p.Y))).Distinct().Count();
            Assert.That(tiles, Is.EqualTo(freight.Items.Count), "Freight crates were stacked on one tile.");
            Assert.That(contracts.TryAbandon(holder, freight.Id), Is.True);
        });

        // A higher-tier bounty flies the Reaver line for its tier.
        await server.WaitAssertion(() =>
        {
            var gunship = Take(contracts, mob, board, "WFContractMarshalGunship", crewCheck: false);
            var run = entMan.GetComponent<Content.Server._WF.Encounters.Components.WFEncounterComponent>(gunship.EncounterEntity!.Value);
            Assert.That(run.Tier, Is.EqualTo(4), "The bounty's encounter did not take the contract's tier.");
            Assert.That(run.Ships["target"].Vessel?.Id, Is.AnyOf("Kalisto", "Metis", "Mudskipper", "Scallywag"),
                "A tier 4 gunship bounty should fly a tier 4 Reaver hull.");
            Assert.That(contracts.TryAbandon(holder, gunship.Id), Is.True);
        });

        // Twin survey: two derelicts, anomalies on both. Chain links never post on their own.
        await server.WaitAssertion(() =>
        {
            var twin = Take(contracts, mob, board, "WFContractLabTwinSurvey", crewCheck: false);
            Assert.That(twin.ExtraWrecks, Has.Count.EqualTo(1));
            var grids = twin.Anomalies.Select(a => entMan.GetComponent<TransformComponent>(a).GridUid).Distinct().Count();
            Assert.That(grids, Is.EqualTo(2), "The twin survey's anomalies were not spread over both derelicts.");
            Assert.That(contracts.TryAbandon(holder, twin.Id), Is.True);
            Assert.That(contracts.All.Any(c => c.Prototype == "WFContractMarshalsWarlordChain"), Is.False,
                "A chain link was posted without its first link.");
        });

        // Ammunition drive: full magazines and ammo boxes of any calibre count; short or empty magazines and guns don't.
        await server.WaitAssertion(() =>
        {
            var ammo = new WFContract { Prototype = "WFContractTSFFactionAmmo", Goods = { (0, 6) } };
            var at = new EntityCoordinates(ContractFlowTest.Largest(server, central), 1.5f, 1.5f);
            var crate = entMan.SpawnEntity("CrateGenericSteel", at);
            var storage = entMan.System<Content.Shared.Storage.EntitySystems.SharedEntityStorageSystem>();
            foreach (var item in new[] { "Magazine9x19mmPistolFMJ", "AmmoBox9x19mmFMJ", "Magazine9x19mmPistolEmpty", "WeaponShotgunKammerer" })
            {
                Assert.That(storage.Insert(entMan.SpawnEntity(item, at), crate), Is.True);
            }

            var shortMag = entMan.SpawnEntity("Magazine9x19mmPistolFMJ", at);
            var provider = entMan.GetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(shortMag);
            Assert.That(provider.UnspawnedCount, Is.GreaterThan(0), "Precondition: the magazine has unspawned rounds to take.");
            provider.UnspawnedCount--;
            Assert.That(storage.Insert(shortMag, crate), Is.True);
            Assert.That(contracts.Packed(ammo, crate).Single(), Is.EqualTo(2), "Only the full magazine and the box count.");
        });

        // Buoys: one per point, counted only once set adrift in open space near an unlogged point.
        WFContract buoys = default!;
        EntityUid packing = default;
        var placed = new System.Collections.Generic.List<EntityUid>();
        var crates = entMan.System<Content.Shared.Storage.EntitySystems.SharedEntityStorageSystem>();
        await server.WaitAssertion(() =>
        {
            buoys = Take(contracts, mob, board, "WFContractCaelNavBuoys");
            Assert.That(buoys.Items, Has.Count.EqualTo(buoys.Count));
            Assert.That(buoys.Points, Has.Count.EqualTo(buoys.Count));
            // At the point but packed in a crate floating there: not adrift.
            packing = entMan.SpawnEntity("CrateGenericSteel", buoys.Points[0]);
            Assert.That(crates.Insert(buoys.Items[0], packing), Is.True);
        });
        await pair.RunTicksSync(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(buoys.Logged, Is.Empty, "A packed buoy was counted.");
            Assert.That(crates.Remove(buoys.Items[0], packing), Is.True);
            placed.AddRange(buoys.Items);
            for (var i = 0; i < buoys.Items.Count; i++)
            {
                var point = buoys.Points[i];
                xforms.SetMapCoordinates(buoys.Items[i], new MapCoordinates(point.Position + new Vector2(20f, 0f), point.MapId));
            }
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(buoys.Status, Is.EqualTo(WFContractStatus.Completed), "Buoys set adrift at every point did not complete.");
            Assert.That(placed.All(b => !entMan.Deleted(b) && !entMan.IsQueuedForDeletion(b)), Is.True,
                "Set buoys were cleared away as soon as the contract completed.");
        });

        // Picket: arriving sends the raiders; holding the point long enough completes it and sends them away.
        WFContract picket = default!;
        await server.WaitAssertion(() =>
        {
            picket = Take(contracts, mob, board, "WFContractMarshalsPicket");
            Assert.That(picket.Site, Is.Not.Null);
            Assert.That(picket.EncounterEntity, Is.Null, "Raiders were sent before anyone arrived.");
            // Earlier stages leave the test character hurt; the picket wants them conscious.
            entMan.System<Content.Server.Administration.Systems.RejuvenateSystem>().PerformRejuvenate(mob);
            xforms.SetMapCoordinates(mob, picket.Site!.Value);
        });
        await pair.RunTicksSync(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(picket.Started, Is.True, "Reaching the point did not start the picket.");
            Assert.That(picket.EncounterEntity, Is.Not.Null, "No raiders were sent at the picket.");
            picket.Progress = System.TimeSpan.FromMinutes(6) - System.TimeSpan.FromSeconds(1);
        });
        await pair.RunTicksSync(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(picket.Status, Is.EqualTo(WFContractStatus.Completed), "Holding the point did not complete the picket.");
            var encounter = entMan.GetComponentOrNull<Content.Server._WF.Encounters.Components.WFEncounterComponent>(picket.EncounterEntity!.Value);
            Assert.That(encounter == null || encounter.Resolution != null, "The raiders were left active after the picket.");
        });

        // Rescue: every stasis bag must come in; one alone doesn't finish it.
        WFContract rescue = default!;
        await server.WaitAssertion(() =>
        {
            rescue = Take(contracts, mob, board, "WFContractMarshalsRescue", crewCheck: false);
            Assert.That(rescue.Items, Has.Count.GreaterThanOrEqualTo(3));
            xforms.SetCoordinates(rescue.Items[0], new EntityCoordinates(ContractFlowTest.Largest(server, central), 1.5f, 1.5f));
        });
        await pair.RunTicksSync(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(rescue.Status, Is.EqualTo(WFContractStatus.Active), "One survivor finished a rescue.");
            Assert.That(rescue.Delivered, Is.EqualTo(1));
            foreach (var bag in rescue.Items.Skip(1))
            {
                xforms.SetCoordinates(bag, new EntityCoordinates(ContractFlowTest.Largest(server, central), 1.5f, 1.5f));
            }
        });
        await pair.RunTicksSync(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(rescue.Status, Is.EqualTo(WFContractStatus.Completed), "Every survivor brought in did not finish the rescue.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every Caelestinus marshal bounty puts its target in the world when taken (feedback #523).</summary>
    [Test]
    public async Task BountiesStart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var contracts = entMan.System<WFContractSystem>();

        await server.WaitAssertion(() =>
        {
            var central = ContractFlowTest.Station(server, map, Vector2.Zero, "Frontier");
            // Neighbours 3-5 km out crowd the bounty band, as round-start stations do.
            ContractFlowTest.Station(server, map, new Vector2(4000f, 0f), "Sevastopol");
            ContractFlowTest.Station(server, map, new Vector2(0f, 3000f), "Hokkaido");
            ContractFlowTest.Station(server, map, new Vector2(-3500f, -2000f), "AnomalousLab");
            var board = entMan.SpawnEntity("WFContractBoard", new EntityCoordinates(ContractFlowTest.Largest(server, central), 4.5f, 4.5f));
            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
            var minds = entMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Test Hunter");
            minds.TransferTo(mind.Owner, mob);

            var bounties = server.ProtoMan.EnumeratePrototypes<WFContractPrototype>()
                .Where(p => p.Contractor.Id == "WFContractorCaelMarshals" && p.Objective is WFContractBounty && p.Weight > 0)
                .Select(p => p.ID)
                .ToList();
            Assert.That(bounties, Is.Not.Empty);
            foreach (var id in bounties)
            {
                var bounty = Take(contracts, mob, board, id, crewCheck: false);
                Assert.That(bounty.EncounterEntity, Is.Not.Null, $"{id} started with no target.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Posts an offer, waives its deposit (the test character has no bank) and takes it.</summary>
    private static WFContract Take(WFContractSystem contracts, EntityUid mob, EntityUid board, string id, bool crewCheck = true)
    {
        var offer = contracts.PostOffer(id)!;
        Assert.That(offer, Is.Not.Null, $"{id} could not be posted.");
        offer.Deposit = 0;
        if (!crewCheck)
        {
            Assert.That(contracts.ForceAccept(mob, offer.Id, board), Is.True, $"{id} did not start.");
            return offer;
        }

        Assert.That(contracts.TryAccept(mob, offer.Id, board), Is.Null, $"{id} was not accepted.");
        return offer;
    }
}
