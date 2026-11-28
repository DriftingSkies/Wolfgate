#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>
/// A live chamber can point at a round that no longer exists: a client holds one between deleting its predicted round
/// and the server's state, or while the round's own state is still on its way. Reloading from or emptying such a
/// cylinder used to throw, and a throw inside a predicted reload took the client down.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedGunSystem))]
public sealed class RevolverStaleRoundTest
{
    private const string Revolver = "WeaponRevolverPython";

    /// <summary>Spawns a revolver whose chamber at the given offset from the current one holds a deleted round.</summary>
    private static (EntityUid Revolver, RevolverAmmoProviderComponent Comp, int Index) Stale(IEntityManager entMan, TestMapData map, int offset)
    {
        var revolver = entMan.SpawnEntity(Revolver, map.GridCoords);
        var comp = entMan.GetComponent<RevolverAmmoProviderComponent>(revolver);
        var gone = entMan.SpawnEntity(comp.FillPrototype, map.GridCoords);
        entMan.DeleteEntity(gone);

        var index = (comp.CurrentIndex + offset + comp.Capacity) % comp.Capacity;
        comp.AmmoSlots[index] = gone;
        comp.Chambers[index] = true;
        return (revolver, comp, index);
    }

    /// <summary>Taking a round to move it elsewhere, as a reload from the revolver does, gives a fresh round.</summary>
    [Test]
    public async Task TransferFromStaleChamber()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // The transfer takes the round that would be fired last first, one behind the current chamber.
            var (revolver, comp, index) = Stale(entMan, map, -1);
            var ammo = new List<(EntityUid? Entity, IShootable Shootable)>();

            Assert.DoesNotThrow(() =>
                entMan.EventBus.RaiseLocalEvent(revolver, new TakeAmmoEvent(1, ammo, map.GridCoords, null)));

            Assert.Multiple(() =>
            {
                Assert.That(ammo, Has.Count.EqualTo(1), "The transfer gave no round.");
                Assert.That(ammo.All(a => a.Entity is { } round && entMan.EntityExists(round)), "The transfer gave a round that doesn't exist.");
                Assert.That(comp.AmmoSlots[index], Is.Null, "The chamber still points at the round.");
                Assert.That(comp.Chambers[index], Is.Null, "The chamber still reads as loaded.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Emptying the cylinder clears a stale chamber without touching the round.</summary>
    [Test]
    public async Task EmptyStaleChamber()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var guns = entMan.System<SharedGunSystem>();

        await server.WaitAssertion(() =>
        {
            var (revolver, comp, _) = Stale(entMan, map, 0);

            Assert.DoesNotThrow(() => guns.EmptyRevolver(revolver, comp));

            Assert.Multiple(() =>
            {
                Assert.That(comp.AmmoSlots.All(slot => slot == null), "A chamber still points at a round.");
                Assert.That(comp.Chambers.All(chamber => chamber == null), "A chamber still reads as loaded.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
