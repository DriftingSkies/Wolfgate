using System.Collections.Generic;
using Content.Shared._Mono.AmmoLoader;
using Content.Shared.DeviceLinking;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>
/// A single-round ship gun linked to an ammo loader takes the next round from it after firing.
/// </summary>
public sealed class WFLoaderAutoFeedTest
{
    [TestCase("WeaponTurretCharonReload", "CharonSlugAmmo")]
    [TestCase("WeaponTurretR300", "R300ShellHE")]
    public async Task ReloadsFromLinkedLoader(string gunProto, string ammoProto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var xforms = entMan.System<SharedTransformSystem>();
        var links = entMan.System<SharedDeviceLinkSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var guns = entMan.System<SharedGunSystem>();

        EntityUid gun = default;
        EntityUid loader = default;

        await server.WaitAssertion(() =>
        {
            gun = entMan.SpawnEntity(gunProto, map.GridCoords);
            loader = entMan.SpawnEntity("AmmoLoader", map.GridCoords);
            if (!entMan.GetComponent<TransformComponent>(loader).Anchored)
                xforms.AnchorEntity(loader);
            Assert.That(entMan.GetComponent<TransformComponent>(gun).Anchored);

            links.SaveLinks(null, loader, gun, new List<(string, string)> { ("AmmoLoaderLoad", "SpaceArtilleryLoad") });

            var loaderComp = entMan.GetComponent<AmmoLoaderComponent>(loader);
            Assert.That(containers.Insert(entMan.SpawnEntity(ammoProto, map.GridCoords), loaderComp.Container));

            var ammo = entMan.GetComponent<BallisticAmmoProviderComponent>(gun);
            Assert.That(ammo.Count, Is.EqualTo(1));

            var gunComp = entMan.GetComponent<GunComponent>(gun);
            guns.AttemptShoot(gun, gun, gunComp, new EntityCoordinates(map.MapUid, new System.Numerics.Vector2(0, 50)));
            Assert.That(ammo.Count, Is.EqualTo(0), "the shot didn't use the chambered round");
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var ammo = entMan.GetComponent<BallisticAmmoProviderComponent>(gun);
            Assert.That(ammo.Count, Is.EqualTo(1), "the gun didn't reload from the loader");
            Assert.That(entMan.GetComponent<AmmoLoaderComponent>(loader).Container.ContainedEntities, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
