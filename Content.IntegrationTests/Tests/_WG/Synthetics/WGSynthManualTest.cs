#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Tests._WG.Wolfmed;
using Content.Server._WG.Synthetics;
using Content.Server.Station.Systems;
using Content.Shared._WG.Biotype;
using Content.Shared.GameTicking;
using Content.Shared.Guidebook;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Storage;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Synthetics;

/// <summary>
/// "Synths and You": every synthetic spawns with exactly one copy in its backpack, an organic character with none,
/// and the guide page it opens is registered, readable and made of entities that exist.
/// </summary>
[TestFixture]
public sealed class WGSynthManualTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly Regex Embed = new("<GuideEntityEmbed Entity=\"([^\"]+)\"");
    private const string Job = "WGSynthManualTester";
    private static readonly ProtoId<GuideEntryPrototype> GuideId = "WGSynthsAndYou";
    private static readonly ProtoId<GuideEntryPrototype> MedicalGuideId = "Medical";

    [TestPrototypes]
    private const string Prototypes = @"
- type: playTimeTracker
  id: WGPlayTimeSynthManualTester

- type: loadout
  id: WGSynthManualTesterGear
  previewEntity: ClothingUniformJumpsuitColorGrey
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey
    back: ClothingBackpack

- type: loadoutGroup
  id: WGSynthManualTesterGroup
  name: generic-unknown
  loadouts:
  - WGSynthManualTesterGear

- type: roleLoadout
  id: JobWGSynthManualTester
  groups:
  - WGSynthManualTesterGroup

- type: job
  id: WGSynthManualTester
  playTimeTracker: WGPlayTimeSynthManualTester
";

    /// <summary>A spawned synthetic, of any species, ends up with the book in its pack and a repeat adds no second.</summary>
    [TestCase("Human")]
    [TestCase("IPC")]
    [TestCase("Synth")]
    public async Task SyntheticSpawnsWithManualTest(string species)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var before = BookCount();
            var profile = HumanoidCharacterProfile.RandomWithSpecies(species).WithBiotype(WGBiotype.Synthetic);
            var mob = SEntMan.System<StationSpawningSystem>()
                .SpawnPlayerMob(map.GridCoords, job: Job, profile, station: null);

            Assert.That(SEntMan.System<InventorySystem>().TryGetSlotEntity(mob, "back", out var pack),
                "The test job's backpack was not worn.");
            Assert.That(BookCount(), Is.EqualTo(before), "The book exists before the spawn event.");

            SpawnComplete(mob, profile);
            SpawnComplete(mob, profile);

            Assert.Multiple(() =>
            {
                Assert.That(BookCount() - before, Is.EqualTo(1), "A synthetic should have exactly one book.");
                var stored = SEntMan.GetComponent<StorageComponent>(pack!.Value).Container.ContainedEntities
                    .Count(e => SEntMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == WGSynthManualSystem.Book.Id);
                Assert.That(stored, Is.EqualTo(1), "The book is not in the backpack.");
            });

            SEntMan.DeleteEntity(mob);
        });
    }

    /// <summary>A synthetic with no backpack holds the book instead.</summary>
    [Test]
    public async Task NoBackpackFallsBackToHandTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var before = BookCount();
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WGBiotype.Synthetic);
            var mob = SEntMan.System<StationSpawningSystem>()
                .SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);

            Assert.That(SEntMan.System<InventorySystem>().TryGetSlotEntity(mob, "back", out _), Is.False);

            SpawnComplete(mob, profile);

            Assert.That(BookCount() - before, Is.EqualTo(1));
            var held = SEntMan.System<SharedHandsSystem>().EnumerateHeld(mob).ToList();
            Assert.That(held, Has.Count.EqualTo(1));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(held[0]).EntityPrototype?.ID,
                Is.EqualTo(WGSynthManualSystem.Book.Id));

            SEntMan.DeleteEntity(mob);
        });
    }

    /// <summary>An organic character is handed nothing.</summary>
    [Test]
    public async Task OrganicSpawnsWithoutManualTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var before = BookCount();
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WGBiotype.Organic);
            var mob = SEntMan.System<StationSpawningSystem>()
                .SpawnPlayerMob(map.GridCoords, job: Job, profile, station: null);

            SpawnComplete(mob, profile);

            Assert.That(BookCount(), Is.EqualTo(before), "An organic character should get no book.");
            SEntMan.DeleteEntity(mob);
        });
    }

    /// <summary>The guide entry is a child of Medical, its page is readable, and every entity it embeds exists.</summary>
    [Test]
    public async Task GuidePageIsRegisteredTest()
    {
        await Server.WaitAssertion(() =>
        {
            var protos = SProtoMan;
            var entry = protos.Index(GuideId);
            Assert.That(protos.Index(MedicalGuideId).Children.Select(c => c.Id), Does.Contain(entry.ID));
            Assert.That(protos.HasIndex<EntityPrototype>(WGSynthManualSystem.Book));

            var resources = Server.ResolveDependency<IResourceManager>();
            Assert.That(resources.ContentFileExists(entry.Text), $"{entry.Text} does not exist.");

            using var reader = resources.ContentFileReadText(entry.Text);
            var text = reader.ReadToEnd();
            Assert.That(text.TrimStart(), Does.StartWith("<Document>"));
            Assert.That(text.TrimEnd(), Does.EndWith("</Document>"));

            var embeds = Embed.Matches(text).Select(m => m.Groups[1].Value).ToList();
            Assert.That(embeds, Is.Not.Empty);
            foreach (var id in embeds)
            {
                Assert.That(protos.HasIndex<EntityPrototype>(new EntProtoId(id)), $"The guide embeds {id}, which is not an entity.");
            }
        });
    }

    /// <summary>The ticker raises this at the mob and to everyone; only the mob is needed here.</summary>
    private void SpawnComplete(EntityUid mob, HumanoidCharacterProfile profile)
    {
        var ev = new PlayerSpawnCompleteEvent(mob, default!, Job, false, true, 0, EntityUid.Invalid, profile);
        SEntMan.EventBus.RaiseLocalEvent(mob, ev);
    }

    private int BookCount()
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == WGSynthManualSystem.Book.Id)
                count++;
        }

        return count;
    }
}
