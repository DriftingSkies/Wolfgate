using System.Collections.Generic;
using Content.Shared.Clothing.Components;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Resprites;

/// <summary>Command neckwear resprited or restored by the Resprites module is offered to Vagrants.</summary>
[TestFixture]
public sealed class RespritesLoadoutTest
{
    private static readonly string[] LoadoutIds =
    {
        "ContractorClothingCloakCmo",
        "ContractorClothingNeckMantleCMO",
        "WFContractorClothingNeckCloakHos",
        "WFContractorClothingNeckMantleHOS",
    };

    [Test]
    public async Task CommandNeckwearIsOfferedToVagrants()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var role = proto.Index<RoleLoadoutPrototype>("JobContractor");
            var offered = new HashSet<string>();

            foreach (var groupId in role.Groups)
            {
                if (!proto.TryIndex(groupId, out var group))
                    continue;

                foreach (var loadout in group.Loadouts)
                    offered.Add(loadout.Id);
            }

            Assert.Multiple(() =>
            {
                foreach (var loadoutId in LoadoutIds)
                {
                    Assert.That(proto.HasIndex<LoadoutPrototype>(loadoutId), Is.True, loadoutId);
                    Assert.That(offered, Does.Contain(loadoutId), loadoutId);

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
