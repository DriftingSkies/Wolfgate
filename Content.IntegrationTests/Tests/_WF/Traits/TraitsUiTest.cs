using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client._Mono.Company;
using Content.Client._WF.Traits.UI;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traits;

/// <summary>
/// The lobby character editor shows the card-based trait tab and its checkboxes update the profile.
/// </summary>
[TestFixture]
public sealed class TraitsUiTest
{
    [TestPrototypes]
    private const string ConditionPrototype = """
                                              - type: traitCategory
                                                id: WFUiTestCategory
                                                name: trait-category-quirks

                                              - type: trait
                                                id: WFUiTestMothCondition
                                                name: trait-pacifist-name
                                                description: trait-pacifist-desc
                                                category: WFUiTestCategory
                                                conditions:
                                                - !type:IsSpeciesCondition
                                                  species: Moth

                                              - type: trait
                                                id: WFUiTestServerOnlyWhitelist
                                                name: trait-pacifist-name
                                                description: trait-pacifist-desc
                                                category: WFUiTestCategory
                                                whitelist:
                                                  components:
                                                  - AutoTraitor
                                              """;

    [Test]
    public async Task UnmetConditionsShowPopup()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var editor = new HumanoidProfileEditor(
                client.ResolveDependency<IClientPreferencesManager>(),
                client.ResolveDependency<IConfigurationManager>(),
                client.ResolveDependency<IEntityManager>(),
                client.ResolveDependency<IFileDialogManager>(),
                client.ResolveDependency<ILogManager>(),
                client.ResolveDependency<IPlayerManager>(),
                client.ResolveDependency<IPrototypeManager>(),
                client.ResolveDependency<IResourceCache>(),
                client.ResolveDependency<JobRequirementsManager>(),
                client.ResolveDependency<MarkingManager>(),
                client.ResolveDependency<CompanyManager>());

            editor.SetProfile(HumanoidCharacterProfile.DefaultWithSpecies(), null);
            var entry = Descend(editor.FindControl<TraitsTab>("TraitsTab")!)
                .OfType<TraitEntry>()
                .First(entry => entry.Trait.ID == "WFUiTestMothCondition");

            Assert.That(entry.Visible, Is.True, "A trait with unmet conditions should still be listed.");
            Assert.That(entry.FindControl<CheckBox>("TraitCheckbox")!.Disabled, Is.True,
                "A trait with unmet conditions should be disabled.");

            var tooltip = entry.TooltipSupplier?.Invoke(entry) as Tooltip;
            Assert.That(tooltip?.Text, Does.Contain("Requirements not met"));
            Assert.That(tooltip?.Text, Does.Contain("Moth"));

            // The client can't see a server-only component on the body, so it doesn't block the trait.
            var serverOnly = Descend(editor.FindControl<TraitsTab>("TraitsTab")!)
                .OfType<TraitEntry>()
                .First(entry => entry.Trait.ID == "WFUiTestServerOnlyWhitelist");
            Assert.That(serverOnly.FindControl<CheckBox>("TraitCheckbox")!.Disabled, Is.False,
                "A whitelist on a server-only component should leave the trait selectable.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraitsTabRendersAndToggles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var editor = new HumanoidProfileEditor(
                client.ResolveDependency<IClientPreferencesManager>(),
                client.ResolveDependency<IConfigurationManager>(),
                client.ResolveDependency<IEntityManager>(),
                client.ResolveDependency<IFileDialogManager>(),
                client.ResolveDependency<ILogManager>(),
                client.ResolveDependency<IPlayerManager>(),
                client.ResolveDependency<IPrototypeManager>(),
                client.ResolveDependency<IResourceCache>(),
                client.ResolveDependency<JobRequirementsManager>(),
                client.ResolveDependency<MarkingManager>(),
                client.ResolveDependency<CompanyManager>());

            editor.SetProfile(HumanoidCharacterProfile.DefaultWithSpecies(), null);

            var traitsTab = editor.FindControl<TraitsTab>("TraitsTab");
            Assert.That(traitsTab, Is.Not.Null, "The card-based trait tab is missing from the editor.");
            Assert.That(traitsTab!.FindControl<LineEdit>("SearchBar"), Is.Not.Null);
            var oldList = editor.FindControl<BoxContainer>("TraitsList");
            Assert.That(oldList, Is.Not.Null);
            Assert.That(oldList!.Parent?.Visible, Is.False,
                "The old flat trait list should stay hidden behind the new editor.");

            var entries = Descend(traitsTab).OfType<TraitEntry>().ToList();
            Assert.That(entries, Is.Not.Empty, "The new trait tab should list traits.");

            // Existing traits keep their old restriction fields, and blocked ones stay visible with a reason.
            var nanite = entries.First(entry => entry.Trait.ID == "NaniteRepairDrones");
            Assert.That(nanite.Visible, Is.True, "A species-blacklisted trait should still be listed.");
            Assert.That(nanite.FindControl<CheckBox>("TraitCheckbox")!.Disabled, Is.True,
                "A species-blacklisted trait should be disabled.");
            var naniteTip = nanite.TooltipSupplier?.Invoke(nanite) as Tooltip;
            Assert.That(naniteTip?.Text, Does.Contain("You must not be"),
                "The popup should explain the species restriction.");

            var shadekin = entries.First(entry => entry.Trait.ID == "ShadekinRegeneration");
            Assert.That(shadekin.Visible, Is.True, "A whitelist-blocked trait should still be listed.");
            Assert.That(shadekin.FindControl<CheckBox>("TraitCheckbox")!.Disabled, Is.True,
                "A whitelist-blocked trait should be disabled.");
            var shadekinTip = shadekin.TooltipSupplier?.Invoke(shadekin) as Tooltip;
            Assert.That(shadekinTip?.Text, Does.Contain("Shadekin"),
                "The popup should explain the body requirement.");

            // A whitelist the human body already meets must not block the trait.
            var blindness = entries.First(entry => entry.Trait.ID == "Blindness");
            Assert.That(blindness.FindControl<CheckBox>("TraitCheckbox")!.Disabled, Is.False,
                "A met body whitelist should leave the trait selectable.");

            var heretic = entries.First(entry => entry.Trait.ID == "Heretic");
            var hereticTip = heretic.TooltipSupplier?.Invoke(heretic) as Tooltip;
            Assert.That(hereticTip?.Text, Does.Contain("New players can't take this"),
                "The popup should carry the newbie warning.");

            // Incompatibilities show under the trait even before either one is picked.
            var clown = entries.First(entry => entry.Trait.ID == "Clown");
            Assert.That(clown.NotesText, Does.Contain("Incompatible with").And.Contain("Mime"));
            Assert.That(nanite.NotesText, Does.Contain("You must not be"),
                "A blocking species restriction should be shown inline.");

            // Traits sit in their layout subgroup, and sorting by points orders each subgroup by cost.
            var vigor = entries.First(entry => entry.Trait.ID == "Vigor");
            var tenacity = entries.First(entry => entry.Trait.ID == "Tenacity");
            Assert.That(vigor.Parent, Is.SameAs(tenacity.Parent), "Toughness traits should share a subgroup.");
            Assert.That(vigor.Parent, Is.Not.SameAs(clown.Parent), "Different subgroups should be separate.");

            traitsTab.SetSort(TraitSortMode.CostHigh);
            var costs = vigor.Parent!.Children.OfType<TraitEntry>().Select(e => e.Trait.Cost).ToList();
            Assert.That(costs, Is.Ordered.Descending, "Most points first should order a subgroup by cost.");
            traitsTab.SetSort(TraitSortMode.CostLow);
            costs = vigor.Parent!.Children.OfType<TraitEntry>().Select(e => e.Trait.Cost).ToList();
            Assert.That(costs, Is.Ordered.Ascending, "Fewest points first should order a subgroup by cost.");
            traitsTab.SetSort(TraitSortMode.Name);

            var pacifist = entries.First(entry => entry.Trait.ID == "Pacifist");
            var checkbox = pacifist.FindControl<CheckBox>("TraitCheckbox");
            Assert.That(checkbox, Is.Not.Null);

            var trait = new ProtoId<TraitPrototype>("Pacifist");

            // An edit made elsewhere in the editor must survive a trait toggle.
            editor.Profile = editor.Profile!.WithName("Kept Name");
            Toggle(checkbox, true);
            Assert.That(editor.Profile!.Name, Is.EqualTo("Kept Name"),
                "A trait toggle should not undo other profile edits.");
            Assert.That(editor.Profile!.TraitPreferences, Does.Contain(trait),
                "Selecting a trait in the new tab should update the profile.");

            Toggle(checkbox, false);
            Assert.That(editor.Profile!.TraitPreferences, Does.Not.Contain(trait),
                "Deselecting a trait in the new tab should update the profile.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraitsTabLayoutSurvivesCloseAndReopen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var profile = HumanoidCharacterProfile.DefaultWithSpecies();
        LayoutContainer host = default!;

        await client.WaitPost(() =>
        {
            var editor = new HumanoidProfileEditor(
                client.ResolveDependency<IClientPreferencesManager>(),
                client.ResolveDependency<IConfigurationManager>(),
                client.ResolveDependency<IEntityManager>(),
                client.ResolveDependency<IFileDialogManager>(),
                client.ResolveDependency<ILogManager>(),
                client.ResolveDependency<IPlayerManager>(),
                client.ResolveDependency<IPrototypeManager>(),
                client.ResolveDependency<IResourceCache>(),
                client.ResolveDependency<JobRequirementsManager>(),
                client.ResolveDependency<MarkingManager>(),
                client.ResolveDependency<CompanyManager>());

            host = new LayoutContainer { SetSize = new System.Numerics.Vector2(1366, 768) };
            host.AddChild(editor);
            ui.RootControl.AddChild(host);
            editor.SetProfile(profile, null);

            // A category opened and collapsed, then the creator closed and reopened, with Traits still selected.
            var tabs = editor.FindControl<TabContainer>("TabContainer");
            tabs.CurrentTab = 3;
            var categories = Descend(editor.FindControl<TraitsTab>("TraitsTab")!).OfType<TraitCategory>().ToList();
            Assert.That(categories, Is.Not.Empty);
            categories[0].SetExpanded(false);
            categories[0].SetExpanded(true);
            editor.SetProfile(null, null);
            editor.SetProfile(profile, null);
            tabs.CurrentTab = 3;
        });

        await pair.RunTicksSync(3);

        // Expanding a category after hiding and reopening the whole editor must lay its rows out again.
        await client.WaitPost(() =>
        {
            host.Visible = false;
            host.Visible = true;
            var traitsTab = Descend(host).OfType<TraitsTab>().First();
            var category = Descend(traitsTab).OfType<TraitCategory>().First();
            category.SetExpanded(false);
            category.SetExpanded(true);
        });

        await pair.RunTicksSync(3);

        await client.WaitAssertion(() =>
        {
            var traitsTab = Descend(host).OfType<TraitsTab>().First();
            Assert.That(traitsTab.Size.Y, Is.GreaterThan(0), "The trait tab should still have height after reopening.");
            var categories = Descend(traitsTab).OfType<TraitCategory>().ToList();
            Assert.That(categories, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (var category in categories)
                {
                    Assert.That(category.Size.Y, Is.GreaterThan(0),
                        $"{category.CategoryId} should still have height after reopening.");

                    foreach (var entry in category.Entries)
                    {
                        if (!entry.Visible)
                            continue;

                        Assert.That(entry.Size.Y, Is.GreaterThan(0),
                            $"{entry.Trait.ID} should still have height after reopening.");
                    }
                }
            });
        });

        await client.WaitPost(() => host.Orphan());
        await pair.CleanReturnAsync();
    }

    private static void Toggle(CheckBox checkBox, bool pressed)
    {
        checkBox.Pressed = pressed;
        var toggled = (Action<BaseButton.ButtonToggledEventArgs>) typeof(BaseButton)
            .GetField(nameof(BaseButton.OnToggled), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(checkBox);
        toggled!.Invoke(new BaseButton.ButtonToggledEventArgs(pressed, checkBox, ClickArgs()));
    }

    private static GUIBoundKeyEventArgs ClickArgs()
    {
        return new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, true, default, default);
    }

    private static IEnumerable<Control> Descend(Control control)
    {
        yield return control;
        foreach (var child in control.Children)
        {
            foreach (var descendant in Descend(child))
                yield return descendant;
        }
    }
}
