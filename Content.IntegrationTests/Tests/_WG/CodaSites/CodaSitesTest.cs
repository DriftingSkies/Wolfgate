#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.CodaSites;
using Content.Server.Power.Components;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mind;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.CodaSites;

/// <summary>
/// The Coda's sites load, and their mechanics run: the keeper keeps the sheltered Lighthouse, the Last Anchor holds and
/// fails, a Hospice cot heals, the Orchard's vines creep, the Waiting Room calls a number,
/// the Graveyard's rocks are numbered and the Broadcast Tower's mast stands.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCodaSiteSystem))]
public sealed class CodaSitesTest
{
    private static readonly string[] Sites =
        { "lighthouse", "last_anchor", "hospice", "orchard", "graveyard", "waiting_room", "broadcast_tower" };

    [Test]
    public async Task SitesLoadAndRun()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var map = await pair.CreateTestMap();
        var grids = new Dictionary<string, EntityUid>();
        EntityUid player = default;

        await server.WaitPost(() =>
        {
            var loader = server.System<MapLoaderSystem>();
            for (var i = 0; i < Sites.Length; i++)
            {
                var path = Sites[i] == "hospice" ? "/Maps/_WG/PlanetPois/planetary_hospital.yml" : $"/Maps/_WG/CodaSites/{Sites[i]}.yml";
                Assert.That(loader.TryLoadGrid(map.MapId, new ResPath(path), out var grid,
                    offset: new Vector2(i * 300f, 0f)), Is.True, $"{Sites[i]} should load.");
                grids[Sites[i]] = grid!.Value.Owner;
                if (Sites[i] == "hospice")
                    server.System<WGCodaSiteSystem>().PrepareHospice(grid.Value.Owner);
            }

            em.GetComponent<WGLastAnchorComponent>(Find<WGLastAnchorComponent>(em, grids["last_anchor"])).HoldTime = TimeSpan.FromSeconds(1);
            em.GetComponent<WGLastAnchorComponent>(Find<WGLastAnchorComponent>(em, grids["last_anchor"])).FailTime = TimeSpan.FromSeconds(1);
            foreach (var cot in em.EntityQuery<WGHospiceCotComponent>())
                cot.HealTime = TimeSpan.FromSeconds(1);
            em.GetComponent<WGOrchardRootComponent>(Find<WGOrchardRootComponent>(em, grids["orchard"])).GrowEvery = TimeSpan.FromSeconds(1);
            var board = em.GetComponent<WGWaitingBoardComponent>(Find<WGWaitingBoardComponent>(em, grids["waiting_room"]));
            board.CallEvery = TimeSpan.FromSeconds(1);
            // Only those who have been aboard are called; the test's player has a ticket.
            board.Ticketed.Add(pair.Player!.UserId);

            // A player, later moved to the Orchard's clearing.
            player = em.SpawnEntity("MobHuman", new EntityCoordinates(grids["hospice"], new Vector2(10.5f, 5.5f)));
            var minds = server.System<SharedMindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Test Visitor");
            minds.TransferTo(mind.Owner, player);
        });
        await pair.RunTicksSync(ticks * 6);

        await server.WaitAssertion(() =>
        {
            var keeper = Find<WGLighthouseKeeperComponent>(em, grids["lighthouse"]);
            Assert.That(keeper, Is.Not.EqualTo(EntityUid.Invalid), "The keeper is missing.");
            var inventory = server.System<Content.Shared.Inventory.InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(keeper, "jumpsuit", out _), Is.True, "The keeper should be dressed.");
            var figure = em.SpawnEntity("WGEchoFigure", new EntityCoordinates(grids["hospice"], new Vector2(4.5f, 4.5f)));
            Assert.That(inventory.TryGetSlotEntity(figure, "jumpsuit", out _), Is.True, "An Echo figure should be dressed.");
            Assert.That(em.HasComponent<Content.Shared._WG.Lattice.WGLatticeShelterComponent>(grids["lighthouse"]), Is.True,
                "The Lighthouse should shelter.");
            Assert.That(Find<WGBroadcastTowerComponent>(em, grids["broadcast_tower"]), Is.Not.EqualTo(EntityUid.Invalid));
            var rocks = em.EntityQuery<WGGraveRockComponent, TransformComponent>().Where(e => e.Item2.GridUid == grids["graveyard"]).ToList();
            Assert.That(rocks, Has.Count.GreaterThan(20), "The Graveyard needs its rocks.");
            Assert.That(rocks.Select(r => r.Item1.Index).Distinct().Count(), Is.EqualTo(rocks.Count), "Grave rocks need their own numbers.");

            Assert.That(em.GetComponent<WGWaitingBoardComponent>(Find<WGWaitingBoardComponent>(em, grids["waiting_room"])).Number,
                Is.GreaterThan(0), "The board never called a number.");
        });

        // The Last Anchor switches between holding and failing, and its power goes with it.
        await server.WaitAssertion(() =>
        {
            var core = Find<WGLastAnchorComponent>(em, grids["last_anchor"]);
            var anchor = em.GetComponent<WGLastAnchorComponent>(core);
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(core).NeedsPower, Is.EqualTo(!anchor.Holding));
        });

        // A hurt visitor laid on a Hospice cot is healed; the player at the Orchard draws its vines.
        EntityUid patient = default;
        int vinesBefore = 0;
        await server.WaitPost(() =>
        {
            var cot = Find<WGHospiceCotComponent>(em, grids["hospice"]);
            patient = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(cot).Coordinates);
            var brute = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Blunt");
            server.System<DamageableSystem>().TryChangeDamage(patient, new DamageSpecifier(brute, 30), true);
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(patient, null, cot), Is.True, "The patient couldn't lie down.");

            var root = Find<WGOrchardRootComponent>(em, grids["orchard"]);
            vinesBefore = em.EntityQuery<WGOrchardVineComponent, TransformComponent>().Count(e => e.Item2.GridUid == grids["orchard"]);
            server.System<SharedTransformSystem>().SetCoordinates(player,
                em.GetComponent<TransformComponent>(root).Coordinates.Offset(new Vector2(6f, 0f)));
        });
        await pair.RunTicksSync(ticks * 4);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.Zero, "The cot didn't heal.");
            var vines = em.EntityQuery<WGOrchardVineComponent, TransformComponent>().Count(e => e.Item2.GridUid == grids["orchard"]);
            Assert.That(vines, Is.GreaterThan(vinesBefore), "The vines didn't creep towards the player.");
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid Find<T>(IEntityManager em, EntityUid grid) where T : IComponent
    {
        var query = em.EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return uid;
        }

        return EntityUid.Invalid;
    }
}
