#nullable enable
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Database;
using Content.Shared._WF.Outposts;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>Autosaves only write outposts that changed, and what a save holds can't also be kept in a deposit box.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostHardeningTest
{
    [Test]
    public async Task AutosaveAndStamps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var maps = server.System<SharedMapSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var owner = pair.Player!.UserId;

        async Task Settle()
        {
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
        }

        async Task<DateTime> SavedAt()
        {
            var row = await db.GetOutpostSaveDataAsync(owner, 1);
            Assert.That(row, Is.Not.Null, "The save is stored.");
            return row!.Value.Save.Saved;
        }

        try
        {
            EntityUid outpost = default;
            EntityUid pen = default;
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(OutpostCVars.AutosaveInterval, 1f);
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpost = grid!.Value;
                pen = entMan.SpawnEntity("Pen", maps.GridTileToLocal(outpost, entMan.GetComponent<MapGridComponent>(outpost), new Vector2i(0, 0)));
                Assert.That(outposts.TrySave(user, (outpost, entMan.GetComponent<WFOutpostComponent>(outpost)), 1, out reason), Is.True,
                    $"Saving was refused: {reason}.");
                Assert.That(Guid.TryParse(entMan.GetComponent<WFOutpostComponent>(outpost).Lineage, out _), "The outpost has a lineage.");
            });
            await Settle();

            var first = await SavedAt();
            await pair.RunTicksSync(150);
            await Settle();
            Assert.That(await SavedAt(), Is.EqualTo(first), "An unchanged outpost was autosaved.");

            await server.WaitPost(() =>
                entMan.SpawnEntity("Crowbar", maps.GridTileToLocal(outpost, entMan.GetComponent<MapGridComponent>(outpost), new Vector2i(2, 2))));
            await pair.RunTicksSync(150);
            await Settle();
            var row = await db.GetOutpostSaveDataAsync(owner, 1);
            Assert.That(row!.Value.Save.Saved, Is.GreaterThan(first), "A changed outpost wasn't autosaved.");
            Assert.That(row.Value.Save.Lineage.ToString(), Is.EqualTo(entMan.GetComponent<WFOutpostComponent>(outpost).Lineage));

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<WFSaveOriginComponent>(pen), "What a save holds is stamped.");
                var storage = server.System<SharedStorageSystem>();
                var box = entMan.SpawnEntity("SafetyDepositBoxSmall", new EntityCoordinates(ground, -1.5f, 0.5f));
                Assert.That(storage.CanInsert(box, pen, out _), Is.False, "A deposit box took something saved with an outpost.");
                var fresh = entMan.SpawnEntity("Pen", new EntityCoordinates(ground, -1.5f, 0.5f));
                Assert.That(storage.CanInsert(box, fresh, out _), Is.True, "A deposit box refused a plain pen.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
