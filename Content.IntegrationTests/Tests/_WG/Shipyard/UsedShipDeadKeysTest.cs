#nullable enable
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Prototypes;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Shipyard;

/// <summary>
/// Dictionaries keyed by entities off the hull write each key as "invalid", so two of them used to fail the copy. It then
/// stripped the component and saved a second time.
/// </summary>
[TestFixture]
public sealed class UsedShipDeadKeysTest
{
    [Test]
    public async Task OffHullKeysDontFailTheCopy()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));

            var offHullA = entMan.SpawnEntity(null, new EntityCoordinates(map.MapUid, new Vector2(1000f, 1000f)));
            var offHullB = entMan.SpawnEntity(null, new EntityCoordinates(map.MapUid, new Vector2(1000f, 1000f)));

            var backpack = entMan.SpawnEntity("ClothingBackpack", coords);
            var stored = entMan.GetComponent<StorageComponent>(backpack).StoredItems;
            stored[offHullA] = default;
            stored[offHullB] = default;

            var button = entMan.SpawnEntity("SignalButton", coords);
            var ports = entMan.GetComponent<DeviceLinkSourceComponent>(button).LinkedPorts;
            ports[offHullA] = new();
            ports[offHullB] = new();

            var suit = entMan.SpawnEntity("ClothingOuterHardsuitEVA", coords);
            var pieces = entMan.EnsureComponent<ToggleableClothingComponent>(suit).ClothingUids;
            pieces[offHullA] = "head";
            pieces[offHullB] = "head";

            var shipyard = entMan.System<ShipyardSystem>();
            Assert.That(shipyard.TrySaveShip(grid.Owner, out var data), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<StorageComponent>(backpack), "Storage was stripped from the copy.");
                Assert.That(entMan.HasComponent<DeviceLinkSourceComponent>(button), "DeviceLinkSource was stripped from the copy.");
                Assert.That(entMan.HasComponent<ToggleableClothingComponent>(suit), "ToggleableClothing was stripped from the copy.");
            });

            Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True);
            entMan.DeleteEntity(loaded!.Value);
            entMan.DeleteEntity(grid.Owner);
        });

        await pair.CleanReturnAsync();
    }
}
