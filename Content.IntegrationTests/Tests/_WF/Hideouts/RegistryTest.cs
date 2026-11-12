using System.Linq;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._NF.Bank;
using Content.Server._WF.Hideouts;
using Content.Server._WF.Outposts;
using Content.Server._WF.PlanetPois;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Hideouts;

/// <summary>The registry at Central founds outposts on a world's plots; a module stake lays a shell to furnish by hand.</summary>
[TestFixture]
[TestOf(typeof(WFHideoutRegistrySystem))]
public sealed class RegistryTest
{
    /// <summary>A plot clear of the founding patch, and the world's only one.</summary>
    private static readonly Box2i Plot = new(-2, -6, 14, 6);

    [Test]
    public async Task TheRegistryFoundsOnAPlotForCreditsOnly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var registry = server.System<WFHideoutRegistrySystem>();
        var outposts = server.System<WFOutpostSystem>();
        var hideouts = server.System<WFHideoutSystem>();
        var bank = server.System<BankSystem>();
        var owner = pair.Player.UserId;
        await Fund(pair, user, 100_000);

        try
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(registry.TryFound(user, "WFSurfaceAsclepiu", out var reason), Is.False, "A world with no plots took an outpost.");
                Assert.That(reason, Is.EqualTo("wf-outpost-found-no-plot"));
                bank.TryGetBalance(pair.Player, out var refunded);
                Assert.That(refunded, Is.EqualTo(100_000), "A refused founding kept the money.");

                entMan.EnsureComponent<WFPlanetPoiComponent>(ground).OutpostLots.Add(Plot);
                Assert.That(registry.TryFound(user, "WFSurfaceAsclepiu", out reason), Is.True, $"The registry refused: {reason}.");
                bank.TryGetBalance(pair.Player, out var balance);
                Assert.That(balance, Is.EqualTo(100_000 - hideouts.HideoutRules.FoundPrice), "Founding didn't cost the founding price.");
                Assert.That(outposts.TryGetOutpost(owner, out var outpost), Is.True, "No outpost was founded.");
                var at = server.System<SharedTransformSystem>().GetWorldPosition(outpost.Comp.Console);
                Assert.That(Plot.Contains(new Vector2i((int) MathF.Floor(at.X), (int) MathF.Floor(at.Y))), Is.True,
                    "The outpost isn't on the plot.");
                Assert.That(registry.TryFound(user, "WFSurfaceAsclepiu", out _), Is.False, "A second outpost was founded.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A shell keeps a module's walls, doors and power, packs its machines and pays back its furniture.</summary>
    [Test]
    public async Task AShellLeavesTheFurnishingToTheBuilder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var bank = server.System<BankSystem>();
        await Fund(pair, user, 0);

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground), new EntityCoordinates(ground, site.X, site.Y),
                    out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = new Entity<Content.Server._WF.Outposts.Components.WFOutpostComponent>(grid.Value,
                    entMan.GetComponent<Content.Server._WF.Outposts.Components.WFOutpostComponent>(grid.Value));
                server.System<SharedTransformSystem>().SetWorldRotation(user, Direction.East.ToAngle());
                Assert.That(outposts.TryStampPrefab(user, outpost, new ResPath("/SharedMaps/_WF/Hideouts/quarters.yml"),
                    new EntityCoordinates(ground, 8.5f, 0.5f), Direction.East, out reason, shell: true), Is.True, $"The shell was refused: {reason}.");

                var protos = Children(entMan, grid.Value).Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID).ToList();
                Assert.That(protos, Does.Contain("WFPrefabWallFixed"), "The shell has no walls.");
                Assert.That(protos, Does.Contain("WFPrefabDoor"), "The shell has no door.");
                Assert.That(protos, Does.Not.Contain("Bed"), "The shell came furnished.");
                Assert.That(protos, Does.Not.Contain("WFOutpostCryopod"), "The shell's cryopod was set up.");
                var packs = entMan.EntityQueryEnumerator<MetaDataComponent>();
                var packed = false;
                while (packs.MoveNext(out _, out var meta))
                    packed |= meta.EntityPrototype?.ID == "WFOutpostCryopodPack";
                Assert.That(packed, Is.True, "The cryopod didn't come packed.");
                bank.TryGetBalance(pair.Player, out var balance);
                Assert.That(balance, Is.GreaterThan(0), "The furniture's worth wasn't paid back.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static async Task Fund(Pair.TestPair pair, EntityUid user, int balance)
    {
        var server = pair.Server;
        var owner = pair.Player.UserId;
        var funding = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(owner);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, profile.WithBankBalance(balance));
            server.EntMan.EnsureComponent<BankAccountComponent>(user);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);
    }
}
