using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._Mono.Company;
using Content.Client._WF.Humanoid;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Humanoid;

/// <summary>
/// The creator's applied-markings card hides when its body part has nothing applied and shows again when a marking is
/// added. Every row of the shown card must get its full height instead of stacking on one line.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfgateMarkingPicker))]
public sealed class WolfgateMarkingPickerLayoutTest
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public async Task AppliedCardLayoutTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var markings = client.ResolveDependency<MarkingManager>();
        WolfgateMarkingPicker picker = default!;
        LayoutContainer host = default!;
        var chest = new List<MarkingPrototype>();

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
                markings,
                client.ResolveDependency<CompanyManager>()) { SetSize = new Vector2(1366, 768) };
            host = new LayoutContainer { SetSize = new Vector2(1920, 1080) };
            host.AddChild(editor);
            ui.RootControl.AddChild(host);

            // Nothing applied, so the card starts hidden.
            editor.SetProfile(HumanoidCharacterProfile.RandomWithSpecies("Human").WithCharacterAppearance(
                HumanoidCharacterAppearance.DefaultWithSpecies("Human").WithMarkings(new List<Marking>())), null);
            picker = editor.FindControl<WolfgateMarkingPicker>("Markings");
            var tabs = editor.FindControl<TabContainer>("TabContainer");
            tabs.CurrentTab = editor.FindControl<BoxContainer>("MarkingsTab").GetPositionInParent();

            var buttons = (Dictionary<HumanoidVisualLayers, Button>) typeof(WolfgateMarkingPicker).GetField("_partButtons", Flags)!.GetValue(picker)!;
            var pressed = (System.Delegate) typeof(BaseButton).GetField(nameof(BaseButton.OnPressed), Flags)!.GetValue(buttons[HumanoidVisualLayers.Chest])!;
            pressed.DynamicInvoke(new object[] { null });

            chest.AddRange(Descend(picker).OfType<WolfgateMarkingTile>()
                .Select(t => markings.Markings[t.MarkingId!])
                .Where(m => m.MarkingCategory == MarkingCategories.Chest && !m.ForcedColoring && m.Sprites.Count == 1)
                .Take(2));
        });
        await pair.RunTicksSync(3);
        Assert.That(chest, Has.Count.EqualTo(2), "Need two single-sprite chest markings.");

        var card = (Control) typeof(WolfgateMarkingPicker).GetField("_appliedCard", Flags)!.GetValue(picker)!;
        await client.WaitAssertion(() => Assert.That(card.Visible, Is.False, "The card should start hidden."));

        // On, off (the card hides), then another on (the card shows again at the size it had before).
        var sequence = new[] { chest[0], chest[0], chest[1] };
        for (var i = 0; i < sequence.Length; i++)
        {
            var marking = sequence[i];
            await client.WaitPost(() => picker.Toggle(marking));
            await pair.RunTicksSync(3);
            if (i == 1)
                await client.WaitAssertion(() => Assert.That(card.Visible, Is.False, "The card should hide once nothing is applied."));
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(card.Visible, Is.True);
            foreach (var control in Descend(card).Where(c => c.VisibleInTree && c is not ContainerButton))
            {
                Assert.That(control.Size.Y, Is.GreaterThanOrEqualTo(control.DesiredSize.Y - control.Margin.SumVertical - 2f),
                    $"{control.GetType().Name} was laid out {control.Size.Y} tall instead of {control.DesiredSize.Y}.");
            }
        });

        await client.WaitPost(() => host.Orphan());
        await pair.CleanReturnAsync();
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
