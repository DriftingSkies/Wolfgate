#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.StationRecords.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Content.Shared.StationRecords;
using Content.Shared.Store.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.LiveFixes;

/// <summary>Components in the state that failed live ship copies must save without being stripped.</summary>
[TestFixture]
public sealed class ShipCopySaveTest
{
    [Test]
    public async Task ReportedComponentsSave()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var shipyard = entMan.System<ShipyardSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));

            var lathe = entMan.SpawnEntity("Autolathe", coords);
            var recipe = server.ProtoMan.EnumeratePrototypes<LatheRecipePrototype>().First();
            entMan.GetComponent<LatheComponent>(lathe).Queue.Add(new LatheRecipeBatch(recipe, 0, 1, null));

            var console = entMan.SpawnEntity("Paper", coords);
            // The field is restricted to the console's own system.
            typeof(GeneralStationRecordConsoleComponent).GetField(nameof(GeneralStationRecordConsoleComponent.Filter))!
                .SetValue(entMan.AddComponent<GeneralStationRecordConsoleComponent>(console),
                    new StationRecordsFilter(StationRecordFilterType.Name, "x"));

            var rod = entMan.SpawnEntity("Paper", coords);
            entMan.EnsureComponent<DamageOnInteractComponent>(rod);

            // Same store as CrateSyndicateSurplusBundle; its random contents aren't what's being tested.
            var store = entMan.SpawnEntity("Paper", coords);
            entMan.EnsureComponent<StoreComponent>(store);

            Assert.That(shipyard.TrySaveShip(grid.Owner, out var data), Is.True);
            Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True);

            var copy = new List<EntityUid>();
            var children = entMan.GetComponent<TransformComponent>(loaded!.Value).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                copy.Add(child);
            }

            var loadedLathe = copy.Where(entMan.HasComponent<LatheComponent>).Single();
            Assert.That(entMan.GetComponent<LatheComponent>(loadedLathe).Queue, Is.Empty);
            Assert.That(copy.Count(entMan.HasComponent<GeneralStationRecordConsoleComponent>), Is.EqualTo(1));
            Assert.That(copy.Count(entMan.HasComponent<StoreComponent>), Is.EqualTo(1));
            var loadedRod = copy.Where(entMan.HasComponent<DamageOnInteractComponent>).Single();
            Assert.That(entMan.GetComponent<DamageOnInteractComponent>(loadedRod).Damage, Is.Not.Null);

            entMan.DeleteEntity(loaded.Value);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A store whose owner stays behind must not come back unowned, or the next person to open it could claim it.</summary>
    [Test]
    public async Task OwnedStoreStaysClosedAfterACopy()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        var shipyard = entMan.System<ShipyardSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var store = entMan.SpawnEntity("Paper", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            entMan.EnsureComponent<StoreComponent>(store).AccountOwner = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            // Loading logs the reference it can't resolve as an error, which is expected here.
            var failureLevel = pair.ServerLogHandler.FailureLevel;
            pair.ServerLogHandler.FailureLevel = null;
            var saved = shipyard.TrySaveShip(grid.Owner, out var data);
            EntityUid? loaded = null;
            var added = saved && shipyard.TryAddSavedShip(data!, out loaded);
            pair.ServerLogHandler.FailureLevel = failureLevel;

            Assert.That(added, Is.True);
            StoreComponent? copy = null;
            var children = entMan.GetComponent<TransformComponent>(loaded!.Value).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (entMan.TryGetComponent<StoreComponent>(child, out var comp))
                    copy = comp;
            }

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy!.AccountOwner, Is.Not.Null, "A null owner would let the next opener claim the store.");
            entMan.DeleteEntity(loaded.Value);
        });

        await pair.CleanReturnAsync();
    }
}
