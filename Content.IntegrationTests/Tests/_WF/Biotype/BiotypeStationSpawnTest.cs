#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server.Station.Systems;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared._WF.Biotype;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Biotype;

/// <summary>
/// A station spawn of a synthetic Vulpkanin: the body is converted before the loadout goes on, so the gear is worn,
/// the body is an IPC chassis and it still looks like a Vulpkanin.
/// </summary>
[TestFixture]
[TestOf(typeof(StationSpawningSystem))]
public sealed class BiotypeStationSpawnTest : WolfmedGameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: playTimeTracker
  id: WFPlayTimeBiotypeTester

- type: loadout
  id: WFBiotypeTesterGear
  previewEntity: ClothingUniformJumpsuitColorGrey
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey
    shoes: ClothingShoesColorBlack
    gloves: ClothingHandsGlovesColorBlack

- type: loadoutGroup
  id: WFBiotypeTesterGroup
  name: generic-unknown
  loadouts:
  - WFBiotypeTesterGear

- type: roleLoadout
  id: JobWFBiotypeTester
  groups:
  - WFBiotypeTesterGroup

- type: job
  id: WFBiotypeTester
  playTimeTracker: WFPlayTimeBiotypeTester
";

    [Test]
    public async Task SyntheticVulpkaninTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Vulpkanin").WithBiotype(WFBiotype.Synthetic);

            var mob = SEntMan.System<StationSpawningSystem>()
                .SpawnPlayerMob(map.GridCoords, job: "WFBiotypeTester", profile, station: null);

            var organic = SEntMan.SpawnEntity("MobVulpkanin", map.GridCoords);
            SEntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(organic, profile.WithBiotype(WFBiotype.Organic));
            var native = SEntMan.GetComponent<HumanoidAppearanceComponent>(organic);

            var inventory = SEntMan.System<InventorySystem>();
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(mob);
            var parts = SEntMan.System<SharedBodySystem>().GetBodyChildren(mob).ToList();

            Assert.Multiple(() =>
            {
                foreach (var slot in new[] { "jumpsuit", "shoes", "gloves" })
                    Assert.That(inventory.TryGetSlotEntity(mob, slot, out _), $"Nothing in {slot}.");

                Assert.That(SEntMan.GetComponent<BodyComponent>(mob).Prototype?.Id, Is.EqualTo("IPC"));
                Assert.That(SEntMan.HasComponent<SiliconComponent>(mob));
                Assert.That(SEntMan.GetComponent<WFBiotypeComponent>(mob).Biotype, Is.EqualTo(WFBiotype.Synthetic));
                Assert.That(humanoid.Species.Id, Is.EqualTo("Vulpkanin"));
                Assert.That(humanoid.Biotype, Is.EqualTo(WFBiotype.Synthetic));
                Assert.That(Markings(humanoid), Is.EquivalentTo(Markings(native)),
                    "It wears other markings than an organic Vulpkanin of the same profile.");

                foreach (var (part, component) in parts)
                {
                    if (component.ToHumanoidLayers() == null)
                        continue;

                    Assert.That(component.Species, Is.EqualTo("Vulpkanin"), $"{SEntMan.ToPrettyString(part)} draws another species.");
                }

                Assert.That(ChestLayer(mob), Is.Not.Null.And.EqualTo(ChestLayer(organic)),
                    "The chassis torso does not draw the Vulpkanin chest.");
            });

            SEntMan.DeleteEntity(mob);
            SEntMan.DeleteEntity(organic);
        });
    }

    /// <summary>The base layer the body's torso draws.</summary>
    private string? ChestLayer(EntityUid body)
    {
        var torso = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(p => p.Component.PartType == BodyPartType.Torso).Id;
        return SEntMan.GetComponent<BodyPartAppearanceComponent>(torso).ID?.Id;
    }

    private static string[] Markings(HumanoidAppearanceComponent humanoid) =>
        humanoid.MarkingSet.Markings.Values.SelectMany(m => m).Select(m => m.MarkingId).Order().ToArray();
}
