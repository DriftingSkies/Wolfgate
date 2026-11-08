#nullable enable
#pragma warning disable RA0002 // The tests set up lineages, slots and the round-end switch directly.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// Selling an outpost: to the game for its structures and machines, voiding every save of it; to a player at the
/// console, who then owns it; and at round end, paid later to an owner who was away.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostSaleTest
{
    private const int StartingBalance = 1000;

    /// <summary>
    /// The sale pays the anchored machines and console but not the loose goods, voids both saves of the outpost but not
    /// another lineage's, and leaves the seller and the goods on the ground.
    /// </summary>
    [Test]
    public async Task SellingToTheGamePaysTheStructuresAndVoidsTheLineage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var pricing = server.System<PricingSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var owner = pair.Player!.UserId;
        await Fund(pair, pair.Player!, user, StartingBalance);

        Entity<WFOutpostComponent> outpost = default;
        EntityUid goods = default;
        var other = Guid.NewGuid();
        var expected = 0;

        try
        {
            await server.WaitAssertion(() =>
            {
                outpost = Found(pair, user, ground);
                var rtg = entMan.SpawnEntity("WFOutpostCompactRTG", new EntityCoordinates(outpost, 0.5f, 0.5f));
                Assert.That(entMan.GetComponent<TransformComponent>(rtg).Anchored, Is.True, "Precondition: the RTG stands anchored.");
                goods = entMan.SpawnEntity("SheetSteel", new EntityCoordinates(outpost, 2.5f, 2.5f));
                expected = (int) Math.Ceiling(pricing.GetPrice(rtg) + pricing.GetPrice(outpost.Comp.Console));

                Assert.That(expected, Is.GreaterThan(0), "Precondition: the machines are worth something.");
                Assert.That(pricing.GetPrice(goods), Is.GreaterThan(0), "Precondition: the goods are worth something.");
                Assert.That(outposts.SaleValue((outpost, entMan.GetComponent<MapGridComponent>(outpost))),
                    Is.EqualTo(expected).Within(1), "The sale value isn't its anchored machines.");

                Assert.That(outposts.TrySave(user, outpost, 0, out var reason), Is.True, reason);
            });
            await Settle(pair, outposts, owner);
            await server.WaitAssertion(() => Assert.That(outposts.TrySave(user, outpost, 1, out var reason), Is.True, reason));
            await Settle(pair, outposts, owner);
            await server.WaitAssertion(() =>
            {
                // Another lineage's save, which the sale must leave alone.
                var lineage = outpost.Comp.Lineage;
                outpost.Comp.Lineage = other.ToString();
                Assert.That(outposts.TrySave(user, outpost, 2, out var reason), Is.True, reason);
                outpost.Comp.Lineage = lineage;
                outpost.Comp.Slot = 1;
            });
            await Settle(pair, outposts, owner);
            Assert.That(await db.GetOutpostSavesAsync(owner), Has.Count.EqualTo(3), "Precondition: three saves.");

            await server.WaitAssertion(() =>
            {
                entMan.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(outpost, 1.5f, 2.5f));
                Assert.That(outposts.TrySellToGame(user, outpost, out var reason), Is.True, $"The sale was refused: {reason}.");
                Assert.That(entMan.Deleted(outpost), Is.True, "The outpost still stands.");
                Assert.That(Balance(pair, user), Is.EqualTo(StartingBalance + expected), "The seller wasn't paid the sale value.");
                Assert.That(entMan.GetComponent<TransformComponent>(goods).ParentUid, Is.EqualTo(ground), "The goods aren't on the ground.");
                Assert.That(entMan.GetComponent<TransformComponent>(user).ParentUid, Is.EqualTo(ground), "The seller isn't on the ground.");
            });
            await Settle(pair, outposts, owner);

            var left = await db.GetOutpostSavesAsync(owner);
            Assert.That(left.Select(s => s.Lineage), Is.EqualTo(new[] { other }), "The sale didn't void exactly the outpost's saves.");
        }
        finally
        {
            await DeleteSaves(db, owner);
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A player at the console buys the outpost on the owner's offer: the price moves between their banks and the
    /// outpost is theirs under a new lineage. Another player aboard holds a sale to the game back.
    /// </summary>
    [Test]
    public async Task APlayerBuysTheOutpostOnAnOffer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var buyerSession = await server.AddDummySession();
        await PoolManager.WaitUntil(server, () => buyerSession.Status == SessionStatus.InGame && prefs.HavePreferencesLoaded(buyerSession),
            maxTicks: 600);

        EntityUid buyer = default;
        await server.WaitPost(() =>
        {
            buyer = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, OutpostFoundingTest.Site.X, OutpostFoundingTest.Site.Y + 2f));
            server.PlayerMan.SetAttachedEntity(buyerSession, buyer);
        });
        await Fund(pair, pair.Player!, user, StartingBalance);
        await Fund(pair, buyerSession, buyer, 10_000);

        try
        {
            await server.WaitAssertion(() =>
            {
                var outpost = Found(pair, user, ground);
                var console = (outpost.Comp.Console, entMan.GetComponent<WFOutpostConsoleComponent>(outpost.Comp.Console));
                var lineage = outpost.Comp.Lineage;
                var transform = server.System<SharedTransformSystem>();

                var standing = entMan.GetComponent<TransformComponent>(buyer).Coordinates;
                transform.SetCoordinates(buyer, new EntityCoordinates(outpost, 0.5f, 0.5f));
                Assert.That(outposts.TrySellToGame(user, outpost, out var aboard), Is.False, "The outpost sold with someone aboard.");
                Assert.That(aboard, Is.EqualTo("wf-outpost-sell-aboard"));
                transform.SetCoordinates(buyer, standing);

                Assert.That(outposts.TryOffer(user, console.Item1, outpost, buyerSession.UserId, 2500, out var reason), Is.True,
                    $"The offer was refused: {reason}.");
                Assert.That(outposts.TryAcceptOffer(buyer, console, outpost, out reason), Is.True, $"The purchase was refused: {reason}.");

                Assert.Multiple(() =>
                {
                    Assert.That(outpost.Comp.Owner, Is.EqualTo(buyerSession.UserId), "The buyer doesn't own the outpost.");
                    Assert.That(outpost.Comp.Lineage, Is.Not.EqualTo(lineage), "The outpost kept the seller's lineage.");
                    Assert.That(outposts.TryGetOutpost(buyerSession.UserId, out _), Is.True);
                    Assert.That(outposts.TryGetOutpost(pair.Player!.UserId, out _), Is.False, "The seller still owns an outpost.");
                    Assert.That(outposts.IsOwnerOf(buyer, outpost.Comp), Is.True);
                    Assert.That(Balance(pair, user), Is.EqualTo(StartingBalance + 2500), "The seller wasn't paid.");
                    Assert.That(Balance(pair, buyer), Is.EqualTo(10_000 - 2500), "The buyer wasn't charged.");
                });

                Assert.That(outposts.TrySellToGame(user, outpost, out reason), Is.False, "The seller sold it again.");
                Assert.That(reason, Is.EqualTo("wf-outpost-console-not-yours"));
            });
        }
        finally
        {
            await server.WaitPost(() => entMan.DeleteEntity(buyer));
            await server.RemoveDummySession(buyerSession);
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An outpost set to sell at round end, with its owner away, is paid to them when they next spawn.</summary>
    [Test]
    public async Task RoundEndSaleIsPaidToAnAwayOwnerOnSpawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var player = pair.Player!;
        await Fund(pair, player, user, StartingBalance);
        var value = 0;

        try
        {
            await server.WaitAssertion(() =>
            {
                var outpost = Found(pair, user, ground);
                outpost.Comp.SellAtRoundEnd = true;
                value = outposts.SaleValue((outpost, entMan.GetComponent<MapGridComponent>(outpost)));
                Assert.That(value, Is.GreaterThan(0), "Precondition: the outpost is worth something.");

                server.PlayerMan.SetAttachedEntity(player, null);
                typeof(WFOutpostSystem).GetMethod("SellAtRoundEnd", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(outposts, null);
                Assert.That(entMan.Deleted(outpost), Is.True, "The outpost wasn't sold.");
            });
            await Settle(pair, outposts, player.UserId);

            // The debt is written in the background; take it once it lands and owe it again for the spawn to pay.
            var owed = new List<(string Name, int Amount)>();
            for (var i = 0; i < 60 && owed.Count == 0; i++)
            {
                await pair.RunTicksSync(5);
                owed = await db.TakeOutpostPayoutsAsync(player.UserId);
            }

            Assert.That(owed.Select(o => o.Amount), Is.EqualTo(new[] { value }), "The away owner isn't owed the sale value.");
            await db.AddOutpostPayoutAsync(player.UserId, owed[0].Name, owed[0].Amount);

            Task payout = Task.CompletedTask;
            await server.WaitPost(() =>
            {
                server.PlayerMan.SetAttachedEntity(player, user);
                payout = (Task) typeof(WFOutpostSystem).GetMethod("PayOut", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(outposts, new object[] { player, user })!;
            });
            await PoolManager.WaitUntil(server, () => payout.IsCompleted, maxTicks: 600);

            await server.WaitAssertion(() =>
                Assert.That(Balance(pair, user), Is.EqualTo(StartingBalance + value), "The away owner wasn't paid on spawning."));
            Assert.That(await db.TakeOutpostPayoutsAsync(player.UserId), Is.Empty, "The payout is still owed.");
        }
        finally
        {
            await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(player, user));
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static Entity<WFOutpostComponent> Found(TestPair pair, EntityUid user, EntityUid ground)
    {
        var entMan = pair.Server.EntMan;
        var site = OutpostFoundingTest.Site;
        Assert.That(pair.Server.System<WFOutpostSystem>().TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
            new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
        return (grid!.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
    }

    /// <summary>Gives a player's selected character a bank balance and their body an account.</summary>
    private static async Task Fund(TestPair pair, ICommonSession session, EntityUid body, int balance)
    {
        var server = pair.Server;
        var funding = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(session.UserId);
            funding = prefs.SetProfile(session.UserId, p.SelectedCharacterIndex,
                ((HumanoidCharacterProfile) p.SelectedCharacter).WithBankBalance(balance));
            server.EntMan.EnsureComponent<BankAccountComponent>(body);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);
    }

    private static int Balance(TestPair pair, EntityUid body)
    {
        pair.Server.System<BankSystem>().TryGetBalance(body, out var balance);
        return balance;
    }

    private static async Task Settle(TestPair pair, WFOutpostSystem outposts, NetUserId owner)
    {
        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(pair.Server, () => !outposts.IsBusy(owner), maxTicks: 600);
    }

    private static async Task DeleteSaves(IServerDbManager db, NetUserId owner)
    {
        foreach (var save in await db.GetOutpostSavesAsync(owner))
            await db.DeleteOutpostSaveAsync(owner, save.Slot);
    }
}
