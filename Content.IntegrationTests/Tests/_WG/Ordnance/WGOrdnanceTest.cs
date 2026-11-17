using System.Linq;
using System.Numerics;
using Content.Server._WG.Defcon;
using Content.Server._WG.Ordnance;
using Content.Server._WG.Warheads;
using Content.Server._WG.Warheads.Components;
using Content.Shared._WG.VehicleShop;
using Content.Shared._WG.Warheads;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Ordnance;

/// <summary>
/// A bought beacon is credited to the terminal's faction, lands a keyed warhead fuzed as set up with its key and a
/// linked switch, the warhead won't arm without its key, arming sends DEFCON to 1, and faction stations get a terminal.
/// </summary>
public sealed class WGOrdnanceTest
{
    [Test]
    public async Task BeaconKeyAndDefcon()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var ordnance = entities.System<WGOrdnanceSystem>();
        var warheads = entities.System<WGWarheadSystem>();
        var defcon = entities.System<WGDefconSystem>();
        var slots = entities.System<ItemSlotsSystem>();
        var maps = entities.System<SharedMapSystem>();
        var xforms = entities.System<SharedTransformSystem>();

        EntityUid station = default;
        await server.WaitAssertion(() =>
        {
            var terminal = entities.SpawnEntity("WGOrdnanceTerminalPDV", map.GridCoords);
            var beacon = entities.SpawnEntity("WGOrdnanceBeaconTactical", map.GridCoords);
            var purchased = new WGVehicleShopPurchasedEvent(EntityUid.Invalid, terminal);
            entities.EventBus.RaiseLocalEvent(beacon, ref purchased);
            var comp = entities.GetComponent<WGOrdnanceBeaconComponent>(beacon);
            Assert.That(comp.Faction, Is.Not.Null.And.Not.Empty, "a purchase wasn't credited to the terminal's faction.");

            // A device bought weighs heavily on DEFCON at once: its score, and a floor of 3.
            defcon.Evaluate(TimeSpan.FromHours(1));
            Assert.That(defcon.Floor, Is.LessThanOrEqualTo(3), "a bought nuclear device didn't hold DEFCON at 3 or worse.");
            Assert.That(defcon.Score, Is.GreaterThanOrEqualTo(40f), "a bought nuclear device barely moved DEFCON.");

            comp.Fuze = WGOrdnanceFuze.Remote;
            comp.Timer = 60f;
            ordnance.Plant((beacon, comp), null);
            var warhead = ordnance.Land((beacon, comp));

            Assert.That(entities.HasComponent<WGWarheadKeyLockComponent>(warhead), Is.True);
            Assert.That(entities.HasComponent<WGTimerFuzeComponent>(warhead), Is.False, "a remote-only warhead kept its timer fuze.");
            Assert.That(entities.HasComponent<WGRemoteFuzeComponent>(warhead), Is.True);
            Assert.That(entities.GetComponent<WGWarheadComponent>(warhead).Timer, Is.EqualTo(60f));
            Assert.That(entities.GetComponent<DeviceLinkSinkComponent>(warhead).LinkedSources, Is.Not.Empty,
                "the dead-man switch isn't linked to the warhead.");

            var key = entities.EntityQuery<WGNukeKeyComponent>().Single(k => k.Warhead == warhead).Owner;
            Assert.That(warheads.KeyIn(warhead, null), Is.False, "a keyed warhead arms with no key in.");
            Assert.That(slots.TryInsert(warhead, "key", key, null), Is.True, "the key doesn't fit its keyhole.");
            Assert.That(warheads.KeyIn(warhead, null), Is.True);

            defcon.SetLevel(5, false, false);
            warheads.SetSafety((warhead, entities.GetComponent<WGWarheadComponent>(warhead)), false, null);
            Assert.That(defcon.Level, Is.EqualTo(1), "arming a warhead didn't send DEFCON to 1.");
            Assert.That(defcon.Evaluate(TimeSpan.FromHours(10)) && defcon.Level != 1, Is.False, "DEFCON fell with a warhead armed.");
            Assert.That(defcon.Floor, Is.EqualTo(1));

            warheads.Disarm((warhead, entities.GetComponent<WGWarheadComponent>(warhead)), null);
            defcon.SetLevel(5, false, false);

            // A faction station gets a terminal once it has floor.
            var grid = maps.CreateGridEntity(map.MapId);
            xforms.SetWorldPosition(grid.Owner, new Vector2(200f, 0f));
            for (var x = 0; x < 3; x++)
            {
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            }

            entities.AddComponent<WGOrdnanceStationComponent>(grid.Owner).Terminal = "WGOrdnanceTerminalTSF";
            station = grid.Owner;
        });

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityQuery<WGOrdnanceTerminalComponent>()
                .Any(t => entities.GetComponent<TransformComponent>(t.Owner).GridUid == station), Is.True,
                "a faction station got no ordnance terminal.");
        });

        await pair.CleanReturnAsync();
    }
}
