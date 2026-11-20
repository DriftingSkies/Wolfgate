using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.Augments.UI;
using Content.Shared._WG.Augments;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>The creator's Augments tab finds an icon for every choice and lays a part's choices out as cards.</summary>
[TestFixture]
[TestOf(typeof(WGAugmentEditor))]
public sealed class AugmentEditorTest
{
    private static IEnumerable<Control> Descendants(Control control)
    {
        return control.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));
    }

    [Test]
    public async Task IconsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var entities = client.ResolveDependency<IEntityManager>();

        await client.WaitAssertion(() =>
        {
            var icons = new WGAugmentIcons();
            var before = entities.EntityCount;
            Assert.Multiple(() =>
            {
                foreach (var augment in proto.EnumeratePrototypes<WGAugmentPrototype>())
                {
                    // Plating and a missing limb install nothing, so there is nothing to draw.
                    if (WGAugmentRules.Installed(augment) != null)
                        Assert.That(icons.Of(augment), Is.Not.Empty, $"{augment.ID} has no icon.");
                }

                foreach (var style in WGAugmentRules.StylesFor(WGAugmentSlot.LeftArm, proto))
                {
                    Assert.That(icons.Of(style, WGAugmentSlot.LeftArm), Has.Count.EqualTo(2), $"{style.ID} draws an arm and a hand.");
                }

                var human = HumanoidCharacterProfile.DefaultWithSpecies("Human");
                Assert.That(icons.Natural(human, WGAugmentSlot.LeftArm), Has.Count.EqualTo(2));
                Assert.That(icons.Natural(human, WGAugmentSlot.Heart), Is.Not.Empty);
            });
            Assert.That(entities.EntityCount, Is.EqualTo(before), "Reading an entity's sprite leaves nothing spawned.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CardsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice>
                {
                    [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleCyborg"),
                })
                .WithAugmentImplants(new HashSet<ProtoId<WGAugmentPrototype>> { "WGAugmentLeftToolArm" });

            var editor = new WGAugmentEditor();
            editor.SetProfile(profile);
            Assert.That(Descendants(editor).OfType<WGAugmentStylePicker>(), Is.Empty, "No part is picked yet.");

            // The loadout lists the arm and its implant; picking the arm lists its choices.
            Assert.That(Descendants(editor).OfType<WGAugmentRowButton>().Count(), Is.EqualTo(2));
            Descendants(editor).OfType<WGAugmentDoll>().Single().Selected = WGAugmentRegion.LeftArm;
            editor.SetProfile(profile);

            var cards = Descendants(editor).OfType<WGAugmentRowButton>().Where(card => card.ToggleMode).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(cards, Has.Count.GreaterThanOrEqualTo(5), "Natural, two arms and the arm's implants.");
                Assert.That(cards.Count(card => card.Pressed), Is.EqualTo(2), "The picked arm and the installed implant are lit.");
                Assert.That(Descendants(editor).OfType<WGAugmentStylePicker>().Count(), Is.EqualTo(1));
                Assert.That(Descendants(editor).OfType<WGAugmentIcon>().Count(), Is.GreaterThanOrEqualTo(cards.Count + 2));
            });
        });

        await pair.CleanReturnAsync();
    }
}
