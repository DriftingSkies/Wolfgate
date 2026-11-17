using System.Linq;
using Content.Client._WG.LegStyle;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared._WG.LegStyle;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.LegStyle;

/// <summary>
/// A networked Vulpkanin, digitigrade by default, loaded with plantigrade legs: the leg layers and the severable leg
/// follow the style on both sides and a clone keeps it. Loading its own legs again takes the sprites back off and
/// fits clothing to them instead.
/// </summary>
[TestOf(typeof(LegStyleRules))]
public sealed class LegStyleBodyTest : InteractionTest
{
    private const string Species = "Vulpkanin";
    private const string Own = "WGLegsVulpkaninDigitigrade";
    private const string Style = "WGLegsVulpkaninPlantigrade";
    private const string LeftLeg = "WGMobVulpkaninLLegPlanti";

    protected override string PlayerPrototype => "MobVulpkanin";

    [Test]
    public async Task PlantigradeBodyTest()
    {
        var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
        var bodies = SEntMan.System<SharedBodySystem>();
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(Species).WithLegStance(LegStance.Plantigrade);

        await Server.WaitAssertion(() =>
        {
            humanoids.LoadProfile(SPlayer, profile);
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(SPlayer);

            Assert.That(humanoid.LegStyle?.Id, Is.EqualTo(Style));
            Assert.That(humanoid.CustomBaseLayers.TryGetValue(HumanoidVisualLayers.LLeg, out var leg), Is.True,
                "The left leg kept the species' sprite.");
            Assert.That(leg.Id, Is.EqualTo(LeftLeg));
            Assert.That(leg.Color, Is.EqualTo(humanoid.SkinColor), "The leg does not match the skin.");

            var part = bodies.GetBodyChildren(SPlayer)
                .First(p => p.Component.PartType == BodyPartType.Leg && p.Component.Symmetry == BodyPartSymmetry.Left);
            Assert.That(SEntMan.GetComponent<BodyPartAppearanceComponent>(part.Id).ID?.Id, Is.EqualTo(LeftLeg),
                "A severed leg would turn digitigrade.");
        });

        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var humanoid = CEntMan.GetComponent<HumanoidAppearanceComponent>(CPlayer);
            Assert.That(humanoid.LegStyle?.Id, Is.EqualTo(Style), "The style did not reach the client.");
            Assert.That(humanoid.BaseLayers[HumanoidVisualLayers.LLeg].ID, Is.EqualTo(LeftLeg),
                "The client draws the species' leg.");

            // Plantigrade legs wear clothing as it is drawn.
            var hat = new DisplacementData();
            Assert.That(CEntMan.System<LegStyleSystem>().GetDisplacement(CPlayer, "jumpsuit", hat), Is.SameAs(hat),
                "Plantigrade legs took a digitigrade fit.");
        });

        await Server.WaitAssertion(() =>
        {
            var clone = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            humanoids.CloneAppearance(SPlayer, clone);
            var cloned = SEntMan.GetComponent<HumanoidAppearanceComponent>(clone);
            Assert.That(cloned.LegStyle?.Id, Is.EqualTo(Style), "The clone dropped the style.");
            Assert.That(cloned.CustomBaseLayers[HumanoidVisualLayers.LLeg].Id, Is.EqualTo(LeftLeg), "The clone dropped the legs.");
            SEntMan.DeleteEntity(clone);

            humanoids.LoadProfile(SPlayer, profile.WithLegStance(LegStance.Default));
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(SPlayer);
            Assert.That(humanoid.LegStyle?.Id, Is.EqualTo(Own), "The species' own legs lost their clothing fit.");
            Assert.That(humanoid.CustomBaseLayers.ContainsKey(HumanoidVisualLayers.LLeg), Is.False,
                "The species' own legs did not come back.");
        });

        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var legs = CEntMan.System<LegStyleSystem>();
            var hat = new DisplacementData();
            Assert.That(CEntMan.GetComponent<HumanoidAppearanceComponent>(CPlayer).LegStyle?.Id, Is.EqualTo(Own));
            Assert.That(legs.GetDisplacement(CPlayer, "jumpsuit", null)?.SizeMaps[32].State, Is.EqualTo("suit_starlight"),
                "Jumpsuits are not fitted to the legs.");
            Assert.That(legs.GetDisplacement(CPlayer, "outerClothing", null), Is.Not.Null, "Hardsuits are not fitted to the legs.");
            Assert.That(legs.GetDisplacement(CPlayer, "shoes", null), Is.Not.Null, "Shoes are not fitted to the legs.");
            Assert.That(legs.GetDisplacement(CPlayer, "head", hat), Is.SameAs(hat), "The legs changed a slot they don't reshape.");
        });
    }
}
