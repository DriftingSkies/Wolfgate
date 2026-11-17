#nullable enable
using System.Linq;
using Content.Server._WG.Clown;
using Content.Server.Atmos.Components;
using Content.Server.Station.Systems;
using Content.Shared._WG.ChatFont;
using Content.Shared._WG.Clown;
using Content.Shared._WG.Mime;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Traits;

/// <summary>
/// Juggling takes up items picked up mid-juggle without leaving anything in the hands, and stops when the clown goes
/// weightless. The Clown trait's font beats the profile's pick, and the Mime trait grants its actions.
/// </summary>
[TestFixture]
[TestOf(typeof(JugglingSystem))]
public sealed class ClownMimeTraitTest
{
    private static readonly ProtoId<TraitPrototype> ClownProto = "Clown";
    private static readonly ProtoId<TraitPrototype> MimeProto = "Mime";

    private const string Item = "WGTestJuggleItem";
    private const string TwoHanded = "WGTestJuggleTwoHanded";
    private const string Iceberg = "WGChatFontIceberg";
    private const string ClownFont = "ComicNeue";

    [TestPrototypes]
    private const string Prototypes = $"""
        - type: entity
          parent: BaseItem
          id: {Item}

        - type: entity
          parent: BaseItem
          id: {TwoHanded}
          components:
          - type: MultiHandedItem
        """;

    private static EntityUid SpawnClown(IEntityManager entMan, IPrototypeManager proto, EntityCoordinates coords)
    {
        var mob = entMan.SpawnEntity("MobHuman", coords);
        // The test map is a vacuum, and any damage ends juggling.
        entMan.RemoveComponent<BarotraumaComponent>(mob);
        entMan.AddComponents(mob, proto.Index<TraitPrototype>(ClownProto).Components, false);
        return mob;
    }

    private static void EnableGravity(IEntityManager entMan, EntityUid grid, EntityUid mob, bool enabled)
    {
        entMan.EnsureComponent<GravityComponent>(grid).Enabled = enabled;
        entMan.System<SharedGravitySystem>().RefreshWeightless(mob);
    }

    [Test]
    public async Task TwoHandedPickupLeavesHandsFreeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var hands = entMan.System<SharedHandsSystem>();
        var clown = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            clown = SpawnClown(entMan, server.ProtoMan, map.GridCoords);
            EnableGravity(entMan, map.Grid, clown, true);

            Assert.That(hands.TryPickupAnyHand(clown, entMan.SpawnEntity(Item, map.GridCoords)));
            Assert.That(hands.TryPickupAnyHand(clown, entMan.SpawnEntity(Item, map.GridCoords)));
            entMan.EventBus.RaiseLocalEvent(clown, new JuggleActionEvent());

            Assert.That(entMan.GetComponent<JugglingActiveComponent>(clown).JuggledItems, Has.Count.EqualTo(2));
            Assert.That(hands.EnumerateHeld(clown), Is.Empty);

            Assert.That(hands.TryPickupAnyHand(clown, entMan.SpawnEntity(TwoHanded, map.GridCoords)));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<JugglingActiveComponent>(clown).JuggledItems, Has.Count.EqualTo(3));
            Assert.That(hands.EnumerateHeld(clown), Is.Empty, "The two-handed item's virtual item should go with it.");
            entMan.DeleteEntity(clown);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeightlessStopsJugglingTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var hands = entMan.System<SharedHandsSystem>();
            var clown = SpawnClown(entMan, server.ProtoMan, map.GridCoords);
            EnableGravity(entMan, map.Grid, clown, true);

            Assert.That(hands.TryPickupAnyHand(clown, entMan.SpawnEntity(Item, map.GridCoords)));
            Assert.That(hands.TryPickupAnyHand(clown, entMan.SpawnEntity(Item, map.GridCoords)));
            entMan.EventBus.RaiseLocalEvent(clown, new JuggleActionEvent());
            Assert.That(entMan.HasComponent<JugglingActiveComponent>(clown));

            EnableGravity(entMan, map.Grid, clown, false);

            Assert.That(entMan.HasComponent<JugglingActiveComponent>(clown), Is.False);
            Assert.That(hands.EnumerateHeld(clown).Count(), Is.EqualTo(2));
            entMan.DeleteEntity(clown);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClownFontBeatsProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human");
            var clown = entMan.System<StationSpawningSystem>()
                .SpawnPlayerMob(map.GridCoords, job: null, profile.WithChatFont(Iceberg), station: null);

            entMan.AddComponents(clown, server.ProtoMan.Index<TraitPrototype>(ClownProto).Components, false);
            Assert.That(entMan.GetComponent<WGChatFontComponent>(clown).Font, Is.EqualTo(ClownFont));

            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(clown, profile);
            Assert.That(entMan.GetComponent<WGChatFontComponent>(clown).Font, Is.EqualTo(ClownFont),
                "Loading a default profile should leave the clown's font.");

            entMan.DeleteEntity(clown);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MimeTraitGrantsActionsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var mime = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponents(mime, server.ProtoMan.Index<TraitPrototype>(MimeProto).Components, false);

            Assert.That(entMan.GetComponent<MimeAbilitiesComponent>(mime).Granted, Has.Count.EqualTo(2));
            entMan.DeleteEntity(mime);
        });

        await pair.CleanReturnAsync();
    }
}
