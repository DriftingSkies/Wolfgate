#nullable enable
using System.Collections.Generic;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Weapons;

/// <summary>
/// The rogue catcher unit hands over one round for one shot. With two ammo providers it handed over two, which fails
/// a debug assertion in the gun system whenever one fires.
/// </summary>
[TestFixture]
public sealed class CatcherAmmoTest
{
    private const string Catcher = "MobRogueSiliconCatcher";

    [Test]
    public async Task ACatcherShotTakesOneRound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var catcher = entMan.SpawnEntity(Catcher, map.GridCoords);
            Assert.That(entMan.HasComponent<GunComponent>(catcher), $"{Catcher} is no longer a gun itself.");

            var shot = new TakeAmmoEvent(1, new List<(EntityUid? Entity, IShootable Shootable)>(), map.GridCoords, catcher);
            entMan.EventBus.RaiseLocalEvent(catcher, shot);
            Assert.That(shot.Ammo, Has.Count.EqualTo(1), $"{Catcher} gave {shot.Ammo.Count} rounds for one shot.");

            foreach (var (round, _) in shot.Ammo)
            {
                if (round is { } spawned)
                    entMan.DeleteEntity(spawned);
            }
            entMan.DeleteEntity(catcher);
        });

        await pair.CleanReturnAsync();
    }
}
