#nullable enable
#pragma warning disable RA0002 // The test builds its site record by hand.
using System.Linq;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetPois;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Shared.Damage;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>Claiming a point of interest as an outpost: what its terminal asks first, and what the outpost sells for.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostPoiClaimTest
{
    [Test]
    public async Task AClaimedSiteSavesAndSellsOnlyForWhatWasAdded()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var terminals = server.System<WFPoiClaimTerminalSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var owner = pair.Player!.UserId;

        var site = new WFPlanetPoiSite
        {
            Name = "wf-poi-merak-outpost",
            Type = "WFPoiTypeAbandonedOutpost",
            Bounds = new Box2i(2, -4, 12, 5),
        };
        EntityUid terminal = default, wall = default, carp = default;
        EntityUid? outpost = null;
        var baseline = 0;

        try
        {
            await server.WaitAssertion(() =>
            {
                entMan.EnsureComponent<WFPlanetPoiComponent>(ground).Sites.Add(site);
                wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, 9.5f, 0.5f));
                Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, Is.True, "Precondition: the site's wall stands.");

                var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
                Assert.That(outposts.SiteTiles((ground, groundGrid), site), Has.Count.GreaterThan(1), "Precondition: the site's floor counts as the site.");
                Assert.That(outposts.SiteGrid(ground, site), Is.Null, "Precondition: the site is built into the ground.");
                terminal = terminals.SpawnTerminal(ground, site) ?? EntityUid.Invalid;
                Assert.That(terminal, Is.Not.EqualTo(EntityUid.Invalid), "The site got no terminal.");
                Assert.That(site.Terminal, Is.EqualTo(terminal));
                var claim = new Entity<WFPoiClaimTerminalComponent>(terminal, entMan.GetComponent<WFPoiClaimTerminalComponent>(terminal));

                Assert.That(outposts.TryClaimSite(user, claim, out _, out var reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-poi-claim-needs-repair"), "An abandoned outpost's terminal should start broken.");
                server.System<DamageableSystem>().SetAllDamage(terminal, entMan.GetComponent<DamageableComponent>(terminal), 0);

                carp = entMan.SpawnEntity("MobCarp", new EntityCoordinates(ground, 4.5f, 2.5f));
                Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(carp).HostileFactions, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared.NPC.Prototypes.NpcFactionPrototype>("NanoTrasen")),
                    "Precondition: a carp is hostile to people.");
                Assert.That(outposts.TryClaimSite(user, claim, out _, out reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-poi-claim-hostiles"), "A site with a carp in it was claimed.");
                entMan.DeleteEntity(carp);

                Assert.That(outposts.TryClaimSite(user, claim, out outpost, out reason), Is.True, $"The claim was refused: {reason}.");
                var grid = outpost!.Value;
                var comp = entMan.GetComponent<WFOutpostComponent>(grid);
                Assert.That(comp.Owner, Is.EqualTo(owner));
                Assert.That(site.ClaimedBy, Is.EqualTo(grid));
                Assert.That(entMan.Deleted(terminal), Is.True, "The terminal should make way for the console.");
                Assert.That(entMan.GetComponent<TransformComponent>(comp.Console).GridUid, Is.EqualTo(grid));
                Assert.That(entMan.GetComponent<TransformComponent>(wall).GridUid, Is.EqualTo(grid), "The wall stayed on the ground.");
                Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, Is.True, "The wall came loose.");
                var maps = server.System<SharedMapSystem>();
                Assert.That(maps.GetTileRef(ground, entMan.GetComponent<MapGridComponent>(ground), new Vector2i(9, 0)).Tile.IsEmpty, Is.True,
                    "The ground under the claimed site wasn't carved away.");
                Assert.That(outposts.IsReserved(ground, new Vector2i(9, 0)), Is.False, "A claimed site is still reserved ground.");

                baseline = comp.ClaimBaseline;
                Assert.That(baseline, Is.GreaterThan(0), "The site's own structures should be worth something.");
                var gridComp = entMan.GetComponent<MapGridComponent>(grid);
                Assert.That(outposts.SaleValue((grid, gridComp)), Is.Zero, "A freshly claimed site sells for more than nothing.");

                var rtg = entMan.SpawnEntity("WFOutpostCompactRTG", new EntityCoordinates(grid, 5.5f, -2.5f));
                Assert.That(entMan.GetComponent<TransformComponent>(rtg).Anchored, Is.True, "Precondition: the RTG stands anchored.");
                var added = (int) Math.Ceiling(server.System<PricingSystem>().GetPrice(rtg));
                Assert.That(outposts.SaleValue((grid, gridComp)), Is.EqualTo(added).Within(1), "The sale doesn't pay just what was added.");

                Assert.That(outposts.TrySave(user, (grid, comp), 0, out reason), Is.True, reason);
            });

            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
            var saves = await db.GetOutpostSavesAsync(owner);
            Assert.That(saves, Has.Count.EqualTo(1));
            var row = await db.GetOutpostSaveDataAsync(owner, 0);
            Assert.That(row, Is.Not.Null);
            Assert.That(WFOutpostSystem.Unpack(row!.Value.Data), Does.Contain($"claimBaseline: {baseline}"),
                "The save lost the claim baseline, so a reload would sell the site's own structures.");
        }
        finally
        {
            foreach (var save in await db.GetOutpostSavesAsync(owner))
                await db.DeleteOutpostSaveAsync(owner, save.Slot);
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
