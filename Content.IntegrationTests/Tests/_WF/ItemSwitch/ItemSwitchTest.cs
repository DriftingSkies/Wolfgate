using System.Collections.Generic;
using Content.Server._WF.ItemSwitch;
using Content.Shared._WF.ItemSwitch;
using Content.Shared._WF.ItemSwitch.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ItemSwitch;

/// <summary>Ensembles switch between their cloak and mantle forms through the item-switch system.</summary>
[TestFixture]
public sealed class ItemSwitchTest
{
    public static readonly string[] Ensembles =
    {
        "ClothingNeckEnsembleBlueshield",
        "ClothingNeckEnsembleChiefEngineerWhite",
        "ClothingNeckEnsembleChiefEngineerGreen",
        "ClothingNeckEnsembleChiefMedicalOfficer",
    };

    [Test]
    public async Task EnsembleSwitchesAndUpdatesClothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<ItemSwitchSystem>();
            var blueShield = entMan.SpawnEntity("ClothingNeckEnsembleBlueshield", MapCoordinates.Nullspace);
            var itemSwitch = entMan.GetComponent<ItemSwitchComponent>(blueShield);
            var clothing = entMan.GetComponent<ClothingComponent>(blueShield);

            Assert.Multiple(() =>
            {
                Assert.That(itemSwitch.State, Is.EqualTo("cloakblackmantle"));
                Assert.That(clothing.EquippedPrefix, Is.EqualTo("cloakblackmantle"));
                Assert.That(itemSwitch.States.Keys, Is.EquivalentTo(new[]
                {
                    "cloakblackmantle",
                    "cloakwhitemantle",
                    "cloakwhite",
                    "cloakblack",
                    "mantle",
                }));
            });

            Assert.That(system.Switch((blueShield, itemSwitch), "mantle"), Is.True);
            Assert.That(itemSwitch.State, Is.EqualTo("mantle"));
            Assert.That(clothing.EquippedPrefix, Is.EqualTo("mantle"));

            entMan.DeleteEntity(blueShield);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryEnsembleHasSwitchStates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in Ensembles)
                {
                    var entity = entMan.SpawnEntity(id, MapCoordinates.Nullspace);
                    Assert.That(entMan.TryGetComponent<ItemSwitchComponent>(entity, out var itemSwitch), Is.True, id);
                    Assert.That(itemSwitch!.States.Count, Is.GreaterThan(1), id);
                    entMan.DeleteEntity(entity);
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryEnsembleIsOfferedByAJob()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var offered = new HashSet<string>();
            foreach (var group in proto.EnumeratePrototypes<LoadoutGroupPrototype>())
            {
                foreach (var loadout in group.Loadouts)
                {
                    if (proto.TryIndex(loadout, out var protoLoadout)
                        && protoLoadout.Equipment.TryGetValue("neck", out var neck))
                        offered.Add(neck.Id);
                }
            }

            Assert.Multiple(() =>
            {
                foreach (var id in Ensembles)
                    Assert.That(offered, Does.Contain(id), id);
            });
        });

        await pair.CleanReturnAsync();
    }
}
