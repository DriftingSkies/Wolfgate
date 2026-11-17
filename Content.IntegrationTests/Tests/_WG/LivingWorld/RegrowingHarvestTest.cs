#nullable enable
using System.Linq;
using Content.Server._WG.LivingWorld;
using Content.Shared._WG.LivingWorld;
using Content.Shared.Interaction;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.LivingWorld;

/// <summary>A regrowing plant yields its loot once, nothing while bare, and again after it regrows.</summary>
[TestFixture]
[TestOf(typeof(WGRegrowingHarvestSystem))]
public sealed class RegrowingHarvestTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: GoonMushrooms
  id: WGTestFloraMushroomPatch
  components:
  - type: WGRegrowingHarvest
    regrowTime: 2
    harvestTime: 0
    table: !type:AllSelector
      children:
      - id: WGWildMushroom

- type: entity
  parent: GoonMushrooms
  id: WGTestFloraCrystal
  components:
  - type: WGRegrowingHarvest
    tool: Digging
    toolTags: [ Pickaxe ]
    harvestTime: 0.5
    table: !type:AllSelector
      children:
      - id: WGWildMushroom
";

    [Test]
    public async Task HarvestsOnceThenRegrows()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid patch = default;
        await server.WaitPost(() => patch = entMan.SpawnEntity("WGTestFloraMushroomPatch", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));

        int Mushrooms() => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WGWildMushroom");
        var harvest = server.System<WGRegrowingHarvestSystem>();
        await server.WaitAssertion(() =>
        {
            var ent = (patch, entMan.GetComponent<WGRegrowingHarvestComponent>(patch));
            harvest.Harvest(ent, null);
            Assert.That(Mushrooms(), Is.EqualTo(1), "Picking yields the loot.");
            Assert.That(ent.Item2.Harvested, Is.True);
            harvest.Harvest(ent, null);
            Assert.That(Mushrooms(), Is.EqualTo(1), "A bare plant yields nothing.");
        });
        // Regrowth is checked once a second, so give it a couple of checks past the two-second regrow.
        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            var ent = (patch, entMan.GetComponent<WGRegrowingHarvestComponent>(patch));
            Assert.That(ent.Item2.Harvested, Is.False, "It should regrow.");
            harvest.Harvest(ent, null);
            Assert.That(Mushrooms(), Is.EqualTo(2), "A regrown plant yields again.");
            Assert.That(entMan.Deleted(patch), Is.False, "Harvesting leaves the plant standing.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task APickaxeChipsWhatAShovelDigs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid crystal = default, miner = default, pickaxe = default;
        await server.WaitPost(() =>
        {
            var coords = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            crystal = entMan.SpawnEntity("WGTestFloraCrystal", coords);
            miner = entMan.SpawnEntity("MobHuman", coords);
            pickaxe = entMan.SpawnEntity("Pickaxe", coords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(miner, pickaxe), Is.True);
            server.System<SharedInteractionSystem>().InteractUsing(miner, pickaxe, crystal, coords, checkCanInteract: false, checkCanUse: false);
        });
        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGRegrowingHarvestComponent>(crystal).Harvested, Is.True, "The pickaxe should have chipped it.");
            Assert.That(entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WGWildMushroom"), Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }
}
