#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Bank;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Storage.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Outposts;
using Content.Shared.Cargo.Components;
using Content.Shared.Preferences;
using Content.Shared.Power.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>An outpost saved in one round comes back in a later one, for what its parts are worth and half again.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostSaveLoadTest
{
    /// <summary>
    /// An outpost grown by a plate, with a locker of crowbars, a charged cell, a door and some cash is saved, then loaded onto a
    /// freshly founded outpost: everything but the cash comes back where it was, the console on the founding console's
    /// tile, and the price is charged once. A second load whose outline is blocked before it lands is refunded.
    /// </summary>
    [Test]
    public async Task SaveAndLoadRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var bank = server.System<BankSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var ticker = server.System<GameTicker>();
        var owner = pair.Player!.UserId;

        var funding = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(OutpostCVars.LoadDelay, 1f);
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(owner);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, profile.WithBankBalance(1_000_000));
            entMan.EnsureComponent<BankAccountComponent>(user);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);

        int Balance()
        {
            bank.TryGetBalance(pair.Player!, out var balance);
            return balance;
        }

        async Task Settle()
        {
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
        }

        // Lets a save made this round be loaded, as if the round had moved on.
        async Task NextRound()
        {
            var row = await db.GetOutpostSaveDataAsync(owner, 0);
            Assert.That(row, Is.Not.Null, "The save is stored.");
            await db.SetOutpostSaveAsync(owner, row!.Value.Save with { SavedRound = ticker.RoundId - 1000, LoadedRound = ticker.RoundId - 1000 }, row.Value.Data);
            await server.WaitPost(() => outposts.ForgetSlots(owner));
            await server.WaitPost(() => outposts.GetSlots(owner));
            await Settle();
        }

        Entity<WFOutpostComponent> Found()
        {
            var site = OutpostFoundingTest.Site;
            Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
            return (grid!.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
        }

        try
        {
            EntityUid first = default;
            int appraisal = 0;
            await server.WaitAssertion(() =>
            {
                var outpost = Found();
                first = outpost.Owner;
                var grid = entMan.GetComponent<MapGridComponent>(first);
                var east = transform.ToMapCoordinates(new EntityCoordinates(first, 3.5f, 1.5f));
                Assert.That(outposts.TryExtend(user, "Plating", transform.ToCoordinates(ground, east), out var reason), Is.True,
                    $"The plate was refused: {reason}.");

                var storage = server.System<SharedEntityStorageSystem>();
                var locker = entMan.SpawnEntity("LockerSteel", maps.GridTileToLocal(first, grid, new Vector2i(0, 0)));
                for (var i = 0; i < 2; i++)
                {
                    var crowbar = entMan.SpawnEntity("Crowbar", maps.GridTileToLocal(first, grid, new Vector2i(0, 0)));
                    Assert.That(storage.Insert(crowbar, locker), "The crowbar goes in the locker.");
                }

                var cell = entMan.SpawnEntity("PowerCellMedium", maps.GridTileToLocal(first, grid, new Vector2i(0, 2)));
                server.System<BatterySystem>().SetCharge(cell, 123f);
                entMan.SpawnEntity("AirlockGlass", maps.GridTileToLocal(first, grid, new Vector2i(3, 1)));
                entMan.SpawnEntity("SpaceCash100", maps.GridTileToLocal(first, grid, new Vector2i(2, 2)));

                // Raid loot, in the locker and on the floor.
                var stashed = entMan.SpawnEntity("WFRaidBreachingMaul", maps.GridTileToLocal(first, grid, new Vector2i(0, 0)));
                entMan.EnsureComponent<WFRaidLootComponent>(stashed);
                Assert.That(storage.Insert(stashed, locker), "The maul goes in the locker.");
                entMan.EnsureComponent<WFRaidLootComponent>(entMan.SpawnEntity("WFRaidBreachingMaul", maps.GridTileToLocal(first, grid, new Vector2i(1, 2))));

                appraisal = outposts.Appraise((first, grid));
                outposts.GetSlots(owner);
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TrySave(user, (first, entMan.GetComponent<WFOutpostComponent>(first)), 0, out var reason), Is.True,
                    $"Saving was refused: {reason}.");
            });
            await Settle();

            var stored = await db.GetOutpostSaveDataAsync(owner, 0);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Value.Save.Appraisal, Is.EqualTo(appraisal));
            Assert.That(stored.Value.Save.Tiles, Is.EqualTo(10));
            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.GetSlots(owner)?[0]?.Name, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(first).EntityName));
                Assert.That(outposts.IsInUse(outposts.GetSlots(owner)![0]!), Is.True, "A save can't be loaded in the round it was made.");
                entMan.DeleteEntity(first);
            });
            await NextRound();

            // The founding console sits on the same tile as the saved one, so the save lands exactly where it stood.
            EntityUid second = default;
            Vector2 consoleAt = default;
            var before = 0;
            await server.WaitAssertion(() =>
            {
                var outpost = Found();
                second = outpost.Owner;
                consoleAt = transform.GetWorldPosition(outpost.Comp.Console);
                before = Balance();
                Assert.That(outposts.TryStartLoad(user, outpost, 0, out var reason), Is.True, $"The load was refused: {reason}.");
            });
            await Settle();

            int price = 0;
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<WFOutpostPendingLoadComponent>(second), "The outline is drawn on the founding outpost.");
                Assert.That(entMan.GetComponent<WFOutpostPendingLoadComponent>(second).Tiles, Has.Count.EqualTo(10));
                // The cash on the floor is not part of the price.
                price = before - Balance();
                Assert.That(price, Is.EqualTo(WFOutpostSystem.LoadPrice(appraisal)));
            });

            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(second), "The founding outpost is gone.");
                Assert.That(outposts.TryGetOutpost(owner, out var loaded), Is.True, "The save is the owner's outpost.");
                var grid = entMan.GetComponent<MapGridComponent>(loaded);
                Assert.That(outposts.CountTiles((loaded, grid)), Is.EqualTo(10));
                Assert.That(transform.GetWorldPosition(loaded.Comp.Console), Is.EqualTo(consoleAt).Using<Vector2>((a, b) => (a - b).Length() < 0.01f),
                    "The console landed on the founding console's tile.");
                Assert.That(entMan.GetComponent<WFOutpostConsoleComponent>(loaded.Comp.Console).Outpost, Is.EqualTo(loaded.Owner));
                Assert.That(loaded.Comp.Portals, Is.Not.Empty, "The portal ring is rebuilt.");
                Assert.That(Balance(), Is.EqualTo(before - price), "The load was charged once.");

                var on = OnGrid(entMan, loaded);
                var lockers = on.Where(e => entMan.HasComponent<EntityStorageComponent>(e)).ToList();
                Assert.That(lockers, Has.Count.EqualTo(1));
                Assert.That(entMan.GetComponent<EntityStorageComponent>(lockers[0]).Contents.ContainedEntities
                    .Count(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "Crowbar"), Is.EqualTo(2),
                    "The locker kept its crowbars, no more and no fewer.");
                var cells = on.Where(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "PowerCellMedium").ToList();
                Assert.That(cells, Has.Count.EqualTo(1));
                Assert.That(entMan.GetComponent<BatteryComponent>(cells[0]).CurrentCharge, Is.EqualTo(123f).Within(0.5f));
                var doors = on.Where(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "AirlockGlass").ToList();
                Assert.That(doors, Has.Count.EqualTo(1));
                Assert.That(entMan.GetComponent<TransformComponent>(doors[0]).Anchored, "The door is still anchored.");
                Assert.That(on.Any(e => entMan.HasComponent<CashComponent>(e)), Is.False, "Cash is never brought back.");
                Assert.That(on.Concat(entMan.GetComponent<EntityStorageComponent>(lockers[0]).Contents.ContainedEntities)
                    .Any(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WFRaidBreachingMaul"), Is.False,
                    "Raid loot is never brought back.");
                Assert.That(on.Count(e => entMan.HasComponent<WFOutpostKitComponent>(e)), Is.EqualTo(1),
                    "The founding console packed back into its kit.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(loaded).EntityPaused, Is.False);
                Assert.That(outposts.IsInUse(outposts.GetSlots(owner)![0]!), Is.True, "A loaded save can't be loaded again this round.");

                entMan.DeleteEntity(loaded);
            });

            // Something built in the outline before it lands calls the load off and refunds it.
            await NextRound();
            EntityUid third = default;
            await server.WaitAssertion(() =>
            {
                var outpost = Found();
                third = outpost.Owner;
                before = Balance();
                Assert.That(outposts.TryStartLoad(user, outpost, 0, out var reason), Is.True, $"The load was refused: {reason}.");
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(Balance(), Is.EqualTo(before - price));
                var east = transform.ToMapCoordinates(new EntityCoordinates(third, 3.5f, 1.5f));
                entMan.SpawnEntity("WallSolid", transform.ToCoordinates(ground, east));
            });
            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(third), Is.False, "The founding outpost is still there.");
                Assert.That(entMan.HasComponent<WFOutpostPendingLoadComponent>(third), Is.False);
                Assert.That(Balance(), Is.EqualTo(before), "The blocked load was refunded.");
                Assert.That(outposts.TryGetOutpost(owner, out var still) && still.Owner == third);
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
