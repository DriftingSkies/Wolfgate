#nullable enable
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Traders;

/// <summary>
/// An NPC that remembers attackers who aren't aboard must not stop its ship being copied for the used lot. Dead ones all
/// write out as the same missing entity, which used to fail the whole save and lose the ship.
/// </summary>
[TestFixture]
public sealed class UsedShipRetaliationSaveTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGRetaliationSaveDummy
  components:
  - type: NPCRetaliation
    attackMemoryLength: 60

- type: entity
  id: WGRetaliationSaveAttacker
  save: false
";

    [Test]
    public async Task ShipWithAngryNpcStillSaves()
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
            var npc = entMan.SpawnEntity("WGRetaliationSaveDummy", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));

            var memories = entMan.GetComponent<NPCRetaliationComponent>(npc).AttackMemories;
            // Attackers that were deleted, and one that is a mob the map loader will not write out.
            var dead1 = entMan.SpawnEntity(null, map.MapCoords);
            var dead2 = entMan.SpawnEntity(null, map.MapCoords);
            memories[dead1] = TimeSpan.FromMinutes(5);
            memories[dead2] = TimeSpan.FromMinutes(5);
            memories[entMan.SpawnEntity("WGRetaliationSaveAttacker", map.MapCoords)] = TimeSpan.FromMinutes(5);
            memories[entMan.SpawnEntity("WGRetaliationSaveAttacker", map.MapCoords)] = TimeSpan.FromMinutes(5);
            entMan.DeleteEntity(dead1);
            entMan.DeleteEntity(dead2);

            Assert.That(shipyard.TrySaveShip(grid.Owner, out var data), Is.True, "The ship should still copy.");
            Assert.That(data, Does.Contain("WGRetaliationSaveDummy"), "The NPC should come with it.");
            Assert.That(data, Does.Not.Contain("attackMemories"), "Attack memories mean nothing on a copy.");
            Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True, "The copy should load.");
            entMan.DeleteEntity(loaded!.Value);
        });

        await pair.CleanReturnAsync();
    }
}
