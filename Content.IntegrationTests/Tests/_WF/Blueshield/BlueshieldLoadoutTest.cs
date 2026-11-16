using System.Collections.Generic;
using Content.Shared.Clothing.Components;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Blueshield;

/// <summary>Blueshield clothing ported from GoobStation is offered to Vagrants and spawns as wearable clothing.</summary>
[TestFixture]
public sealed class BlueshieldLoadoutTest
{
    private const string VagrantRole = "JobContractor";

    private static readonly string[] LoadoutIds =
    {
        "WFContractorClothingUniformJumpsuitBlueshieldOfficer",
        "WFContractorClothingUniformJumpskirtBlueshieldOfficer",
        "WFContractorClothingUniformJumpsuitBlueshieldShirt",
        "WFContractorClothingOuterBlueshieldArmourVest",
        "WFContractorClothingOuterBlueshieldArmoredJacket",
        "WFContractorClothingHeadHatCowboyBlueshield",
        "WFContractorClothingNeckMantleBlueshield",
        "WFContractorClothingNeckEnsembleBlueshield",
        "WFContractorClothingShoesBootsWinterSnowjackBso",
        "WFContractorClothingBeltMilitaryWebbingBSO",
        "WFContractorClothingBackpackBlueshield",
        "WFContractorClothingBackpackDuffelBlueshield",
        "WFContractorClothingBackpackSatchelBlueshield",
        "WFContractorClothingBackpackMessengerBlueshield",
        "WFContractorClothingHeadsetAltBlueshield",
        "WFContractorClothingEyesGlassesBSO",
        "WFContractorClothingMaskGasSwatBSO",
    };

    [Test]
    public async Task EveryBlueshieldItemIsOfferedToVagrants()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var role = proto.Index<RoleLoadoutPrototype>(VagrantRole);
            var offered = new HashSet<string>();

            // The catalog offers a group's own loadouts and those of its subgroups, one level deep.
            foreach (var groupId in role.Groups)
            {
                if (!proto.TryIndex(groupId, out var group))
                    continue;

                foreach (var loadout in group.Loadouts)
                    offered.Add(loadout.Id);

                foreach (var subgroupId in group.Subgroups)
                {
                    if (!proto.TryIndex(subgroupId, out var subgroup))
                        continue;

                    foreach (var loadout in subgroup.Loadouts)
                        offered.Add(loadout.Id);
                }
            }

            Assert.Multiple(() =>
            {
                foreach (var loadoutId in LoadoutIds)
                {
                    Assert.That(proto.HasIndex<LoadoutPrototype>(loadoutId), Is.True, loadoutId);
                    Assert.That(offered, Does.Contain(loadoutId), loadoutId);
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryBlueshieldItemSpawnsAsWearableClothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var loadoutId in LoadoutIds)
                {
                    var loadout = proto.Index<LoadoutPrototype>(loadoutId);
                    foreach (var entityId in loadout.Equipment.Values)
                    {
                        var entity = entMan.SpawnEntity(entityId, MapCoordinates.Nullspace);
                        Assert.That(entMan.HasComponent<ClothingComponent>(entity), Is.True, entityId.Id);
                        entMan.DeleteEntity(entity);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
