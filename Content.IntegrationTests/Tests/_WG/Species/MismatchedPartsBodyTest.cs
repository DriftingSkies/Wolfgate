using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.Species;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Species;

/// <summary>
/// A networked body of a species without hair: the hair the option unlocks on the server is drawn on the client, and
/// a clone of the body keeps it.
/// </summary>
[TestOf(typeof(MismatchedPartsRules))]
public sealed class MismatchedPartsBodyTest : InteractionTest
{
    private const string Species = "Arachnid";

    protected override string PlayerPrototype => "MobArachnid";

    [Test]
    public async Task NetworkedBodyTest()
    {
        var markings = Server.ResolveDependency<MarkingManager>();
        var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
        string hair = default!;

        await Server.WaitAssertion(() =>
        {
            Assert.That(MismatchedPartsRules.IsNative(MarkingCategories.Hair, Species, markings, ProtoMan), Is.False,
                $"{Species} wear hair on their own now; use a species that doesn't.");

            hair = MismatchedPartsTest.FirstDrawable(MarkingCategories.Hair, Species, markings, ProtoMan);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(Species);
            profile = profile.WithCharacterAppearance(profile.Appearance.WithHairStyleName(hair)).WithMismatchedParts(true);
            humanoids.LoadProfile(SPlayer, profile);
        });

        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var humanoid = CEntMan.GetComponent<HumanoidAppearanceComponent>(CPlayer);
            Assert.That(humanoid.MismatchedParts, Is.True, "The option did not reach the client.");

            var rsi = (SpriteSpecifier.Rsi) markings.Markings[hair].Sprites[0];
            var key = $"{hair}-{rsi.RsiState}";
            var sprite = CEntMan.GetComponent<SpriteComponent>(CPlayer);
            Assert.That(CEntMan.System<SpriteSystem>().TryGetLayer((CPlayer, sprite), key, out var layer, false), Is.True,
                $"No {key} layer on the client.");
            Assert.That(layer!.Visible, Is.True, $"{key} is hidden on the client.");
        });

        await Server.WaitAssertion(() =>
        {
            var clone = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            humanoids.CloneAppearance(SPlayer, clone);
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(clone);

            Assert.That(humanoid.MismatchedParts, Is.True, "The clone dropped the option.");
            Assert.That(humanoid.MarkingSet.TryGetCategory(MarkingCategories.Hair, out var worn)
                        && worn.Any(m => m.MarkingId == hair), Is.True, "The clone dropped the hair.");
            SEntMan.DeleteEntity(clone);
        });
    }
}
