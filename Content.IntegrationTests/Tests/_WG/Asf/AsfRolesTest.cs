using System.Collections.Generic;
using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Asf;

/// <summary>Spawns each ASF role with its default loadout and checks the outfit lands in its slots, with nothing left on the floor.</summary>
[TestFixture]
public sealed class AsfRolesTest
{
    /// <summary>Role, then the slots its default loadout and starting gear must fill and with what.</summary>
    private static readonly (string Job, (string Slot, string Item)[] Outfit)[] Outfits =
    [
        ("WGAsfEnvoy",
        [
            ("jumpsuit", "WGAsfClothingUniformService"), ("head", "WGAsfClothingHeadBeretPlumed"),
            ("neck", "WGAsfClothingNeckCapelet"), ("outerClothing", "WGAsfClothingOuterGreatcoat"),
            ("back", "WGAsfClothingBackpack"), ("shoes", "WGAsfClothingShoesBoots"), ("ears", "WGAsfClothingHeadset"),
            ("id", "WGAsfEnvoyPDA"),
        ]),
        ("WGAsfEnforcer",
        [
            ("jumpsuit", "WGAsfClothingUniformService"), ("head", "WGAsfClothingHeadBeret"),
            ("back", "WGAsfClothingBackpack"), ("pocket1", "WeaponDisabler"),
            ("id", "WGAsfEnforcerPDA"),
        ]),
        ("WGAsfFieldResearcher",
        [
            ("jumpsuit", "WGAsfClothingUniformResearcher"),
            ("back", "WGAsfClothingBackpack"), ("id", "WGAsfFieldResearcherPDA"),
        ]),
        ("WGAsfColonist",
        [
            ("jumpsuit", "WGAsfClothingUniformColonist"), ("gloves", "ClothingHandsGlovesColorYellow"),
            ("back", "WGAsfClothingBackpack"), ("id", "WGAsfColonistPDA"),
        ]),
    ];

    [Test]
    public async Task AsfRolesSpawnInTheirOutfits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var spawning = entities.System<StationSpawningSystem>();
        var inventory = entities.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var (job, outfit) in Outfits)
            {
                var profile = HumanoidCharacterProfile.DefaultWithSpecies();
                var mob = spawning.SpawnPlayerMob(map.GridCoords, new ProtoId<JobPrototype>(job), profile, null);

                foreach (var (slot, item) in outfit)
                {
                    Assert.That(inventory.TryGetSlotEntity(mob, slot, out var worn), Is.True, $"{job} has nothing in {slot}.");
                    Assert.That(entities.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype?.ID, Is.EqualTo(item),
                        $"{job} wears the wrong {slot}.");
                }

                // A starting-gear item whose slot a loadout already filled would be left on the floor.
                var loose = new List<string>();
                var children = entities.GetComponent<TransformComponent>(map.Grid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (child != mob && entities.HasComponent<ItemComponent>(child))
                        loose.Add(entities.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID ?? "?");
                }

                Assert.That(loose, Is.Empty, $"{job} spawned with items on the floor: {string.Join(", ", loose)}");
                entities.DeleteEntity(mob);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A Vox ASF member gets the species kit (nitrogen and a breath mask) like any other faction's.</summary>
    [Test]
    public async Task VoxAsfMembersCanBreathe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var spawning = entities.System<StationSpawningSystem>();
        var inventory = entities.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = spawning.SpawnPlayerMob(map.GridCoords, new ProtoId<JobPrototype>("WGAsfColonist"),
                HumanoidCharacterProfile.DefaultWithSpecies("Vox"), null);
            var carried = new List<string>();
            var slots = inventory.GetSlotEnumerator(mob);
            while (slots.NextItem(out var item))
            {
                carried.Add(entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID ?? "?");
            }

            Assert.That(carried.Any(id => id.Contains("Nitrogen")), Is.True,
                $"A Vox colonist spawned without nitrogen: {string.Join(", ", carried)}");
        });

        await pair.CleanReturnAsync();
    }
}
