#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    // Loose things with set prices for looters to weigh up.
    [TestPrototypes]
    private const string AuditRaidPrototypes = @"
- type: entity
  id: WGTestLootCheap
  name: cheap test loot
  components:
  - type: Item
  - type: StaticPrice
    price: 10

- type: entity
  id: WGTestLootValuable
  name: valuable test loot
  components:
  - type: Item
  - type: StaticPrice
    price: 500

- type: entity
  id: WGTestLootPrecious
  name: precious test loot
  components:
  - type: Item
  - type: StaticPrice
    price: 2000
";

    /// <summary>A looter takes the dearest thing left each trip and leaves bedding and cheap clutter where it lies.</summary>
    [Test]
    public async Task LooterTakesValuablesAndLeavesClutter()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var source = await CreateDeck(new Vector2(24, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var work = Server.System<WGCrewWorkSystem>();
            var hands = Server.System<SharedHandsSystem>();
            var xforms = Server.System<SharedTransformSystem>();
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "loot")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            SEntMan.SpawnAtPosition("BedsheetBlue", new EntityCoordinates(source, new Vector2(0.5f, 2.5f)));
            SEntMan.SpawnAtPosition("WGTestLootCheap", new EntityCoordinates(source, new Vector2(1.5f, 2.5f)));
            var valuable = SEntMan.SpawnAtPosition("WGTestLootValuable", new EntityCoordinates(source, new Vector2(2.5f, 2.5f)));
            var precious = SEntMan.SpawnAtPosition("WGTestLootPrecious", new EntityCoordinates(source, new Vector2(3.5f, 2.5f)));

            foreach (var expected in new[] { precious, valuable })
            {
                Assert.That(work.Advance(deck, "loot", WGCrewObjectiveKind.Loot, source), Is.EqualTo("working"));
                Assert.That(work.Destination(crew, out var at), Is.True);
                xforms.SetCoordinates(crew, at);
                work.Perform(crew);
                Assert.That(hands.IsHolding(crew, expected, out _), Is.True, "The dearest thing left is taken first.");
                Assert.That(work.Destination(crew, out var home), Is.True);
                Assert.That(home.EntityId, Is.EqualTo(deck), "Loot is carried back to the raider.");
                xforms.SetCoordinates(crew, home);
                work.Perform(crew);
                Assert.That(hands.IsHolding(crew, expected, out _), Is.False);
            }

            Assert.That(work.Advance(deck, "loot", WGCrewObjectiveKind.Loot, source), Is.EqualTo("complete"),
                "A bedsheet and cheap clutter are not worth a trip.");
        });
    }

    /// <summary>
    /// A raid that cannot go on completes, so the raider casts off: the only loot was given up on, or no hand is
    /// left alive to send.
    /// </summary>
    [Test]
    public async Task LootCompletesWhenTheRaidCannotGoOn()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var source = await CreateDeck(new Vector2(24, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var work = Server.System<WGCrewWorkSystem>();
            var hands = Server.System<SharedHandsSystem>();
            var xforms = Server.System<SharedTransformSystem>();
            var crewSystem = Server.System<WGCrewSystem>();
            var home = new EntityCoordinates(deck, new Vector2(2.5f));
            var crew = crewSystem.SpawnCrewman(WGCrewRoles.Deckhand, home, "stuck")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var loot = SEntMan.SpawnAtPosition("WGTestLootValuable", new EntityCoordinates(source, new Vector2(2.5f)));

            // The only loot is lost on the way home, so its trip fails and it is given up on.
            Assert.That(work.Advance(deck, "stuck", WGCrewObjectiveKind.Loot, source), Is.EqualTo("working"));
            Assert.That(work.Destination(crew, out var at), Is.True);
            xforms.SetCoordinates(crew, at);
            work.Perform(crew);
            Assert.That(hands.TryDrop(crew, loot), Is.True);
            xforms.SetCoordinates(crew, home);
            work.Perform(crew);
            Assert.That(work.Advance(deck, "stuck", WGCrewObjectiveKind.Loot, source), Is.EqualTo("complete"),
                "A raid does not wait on loot it gave up on.");

            // A raid whose only hand is away waits for him, and ends once he is dead.
            var away = crewSystem.SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(source, new Vector2(0.5f)), "away")!.Value;
            SEntMan.GetComponent<HTNComponent>(away).Enabled = false;
            Assert.That(work.Advance(deck, "away", WGCrewObjectiveKind.Loot, source), Is.EqualTo("no-worker"));
            Server.System<MobStateSystem>().ChangeMobState(away, MobState.Dead);
            Assert.That(work.Advance(deck, "away", WGCrewObjectiveKind.Loot, source), Is.EqualTo("complete"),
                "With no hand left alive the raid takes what it has.");
        });
    }

    /// <summary>Loot is put down well inside the raider, not on the deckhand's post beside its airlock.</summary>
    [Test]
    public async Task LootIsStowedClearOfTheAirlock()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 9, gravity: true);
        var source = await CreateDeck(new Vector2(24, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var work = Server.System<WGCrewWorkSystem>();
            var hands = Server.System<SharedHandsSystem>();
            var xforms = Server.System<SharedTransformSystem>();
            // The airlock on the bottom edge, and the deckhand posted just inside it.
            SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(deck, new Vector2(4.5f, 0.5f)));
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(4.5f, 1.5f)), "stow")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var loot = SEntMan.SpawnAtPosition("WGTestLootValuable", new EntityCoordinates(source, new Vector2(2.5f)));

            Assert.That(work.Advance(deck, "stow", WGCrewObjectiveKind.Loot, source), Is.EqualTo("working"));
            Assert.That(work.Destination(crew, out var at), Is.True);
            xforms.SetCoordinates(crew, at);
            work.Perform(crew);
            Assert.That(hands.IsHolding(crew, loot, out _), Is.True);
            Assert.That(work.Destination(crew, out var stash), Is.True);
            Assert.That(stash.EntityId, Is.EqualTo(deck));
            var clearance = Math.Max(Math.Abs(stash.Position.X - 4.5f), Math.Abs(stash.Position.Y - 0.5f));
            Assert.That(clearance, Is.GreaterThanOrEqualTo(3f), $"Loot is put down away from the airlock, not at {stash.Position}.");
            xforms.SetCoordinates(crew, stash);
            work.Perform(crew);
            Assert.That(hands.IsHolding(crew, loot, out _), Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(loot).GridUid, Is.EqualTo(deck));
        });
    }
}
