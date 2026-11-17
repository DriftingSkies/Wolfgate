#nullable enable
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Linq;
using System.Numerics;
using Content.Shared._WG.NpcCrew;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>A server poll preserves the native popup and the exact button receiving a pending mouse release.</summary>
    [TestCase("_grid")]
    [TestCase("_objectiveTarget")]
    [TestCase("_target")]
    public async Task CrewSetupPollingKeepsDropdownClicksAndFilterFocus(string field)
    {
        await WithCrewWindow(window =>
        {
            var first = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "crew" };
            var target = new WGCrewSetupCrew { Grid = new NetEntity(102), Group = "crew" };
            var snapshot = UiSnapshot(first, target);
            snapshot.Grids[0] = new WGCrewSetupGrid(first.Grid, "Alpha");
            snapshot.Grids[1] = new WGCrewSetupGrid(target.Grid, "Beta");
            UiCall(window, "Receive", snapshot);
            if (field != "_grid")
                window.SelectCrew(first.Grid, first.Group);
            if (field == "_target")
            {
                UiField<BoxContainer>(window, "_manageView").Children.OfType<TabContainer>().Single().CurrentTab = 2;
                UiField<OptionButton>(window, "_order").SelectId((int) WGPilotOrder.Dock);
                UiCall(window, "UpdateOrderFields");
            }

            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var options = UiField<OptionButton>(window, field);
            var popup = UiField<Popup>(options, "_popup");
            var filter = UiField<LineEdit>(options, "_filterBox");
            var choices = UiField<BoxContainer>(options, "_popupContentsBox");
            var modalCount = ui.ModalRoot.ChildCount;
            UiCall(options, "TogglePopup", true);
            filter.Text = "Beta";
            ArrangeCrewPopup(popup);
            var choice = choices.Children.OfType<Button>().Single(button => button.Text!.StartsWith("Beta"));
            ui.ControlFocused = choice;
            CrewUiKey(ui, choice, BoundKeyState.Down);

            UiCall(window, "Receive", snapshot);
            var reordered = UiSnapshot(first, target);
            reordered.Grids = new()
            {
                new WGCrewSetupGrid(target.Grid, "Beta renamed"),
                new WGCrewSetupGrid(new NetEntity(103), "Gamma"),
                new WGCrewSetupGrid(first.Grid, "Alpha"),
            };
            UiCall(window, "Receive", reordered);
            Assert.Multiple(() =>
            {
                Assert.That(choice.Parent, Is.SameAs(choices), "Polling must preserve the button that owns the pending mouse release.");
                Assert.That(choice.Text, Does.StartWith("Beta renamed"));
                Assert.That(choice.VisibleInTree, Is.True);
                Assert.That(popup.Visible, Is.True);
                Assert.That(ui.KeyboardFocused, Is.SameAs(filter));
                Assert.That(ui.ControlFocused, Is.SameAs(choice));
                Assert.That(filter.Text, Is.EqualTo("Beta"));
                Assert.That(ui.ModalRoot.ChildCount, Is.EqualTo(modalCount + 1));
            });
            ArrangeCrewPopup(popup);
            CrewUiKey(ui, choice, BoundKeyState.Up);
            Assert.Multiple(() =>
            {
                Assert.That((options.SelectedMetadata as WGCrewSetupGrid)?.Id, Is.EqualTo(target.Grid), "Mouse-up must select the original grid even after its numeric index changed.");
                Assert.That(options.SelectedId, Is.EqualTo(0));
                Assert.That(popup.Parent, Is.Null);
                Assert.That(ui.ModalRoot.ChildCount, Is.EqualTo(modalCount));
                Assert.That(ui.KeyboardFocused, Is.Not.SameAs(filter));
            });

            reordered.Grids.RemoveAt(0);
            UiCall(window, "Receive", reordered);
            Assert.That(options.SelectedId, Is.EqualTo(-1), "A disappearing destination must not silently select another grid.");
        });
    }

    /// <summary>Closing a window with a filtered dropdown open leaves no modal or focus that can intercept other menus.</summary>
    [Test]
    public async Task CrewSetupCloseReleasesDropdownModalAndExternalPopupStillWorks()
    {
        await WithCrewWindow(window =>
        {
            var crew = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "crew" };
            UiCall(window, "Receive", UiSnapshot(crew));
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var options = UiField<OptionButton>(window, "_grid");
            var popup = UiField<Popup>(options, "_popup");
            var filter = UiField<LineEdit>(options, "_filterBox");
            var modalCount = ui.ModalRoot.ChildCount;
            UiCall(options, "TogglePopup", true);
            Assert.That(ui.KeyboardFocused, Is.SameAs(filter));
            window.Close();
            Assert.Multiple(() =>
            {
                Assert.That(popup.Parent, Is.Null);
                Assert.That(popup.Visible, Is.False);
                Assert.That(ui.KeyboardFocused, Is.Not.SameAs(filter));
                Assert.That(ui.ModalRoot.ChildCount, Is.EqualTo(modalCount));
            });

            var pressed = false;
            var externalChoice = new Button { Text = "External menu action" };
            externalChoice.OnPressed += _ => pressed = true;
            var external = new Popup { Children = { externalChoice } };
            try
            {
                ui.ModalRoot.AddChild(external);
                external.Open();
                ArrangeCrewPopup(external);
                ui.ControlFocused = externalChoice;
                CrewUiKey(ui, externalChoice, BoundKeyState.Down);
                CrewUiKey(ui, externalChoice, BoundKeyState.Up);
                Assert.Multiple(() =>
                {
                    Assert.That(externalChoice.VisibleInTree, Is.True);
                    Assert.That(externalChoice.Size.X, Is.GreaterThan(0));
                    Assert.That(externalChoice.Size.Y, Is.GreaterThan(0));
                    Assert.That(pressed, Is.True);
                });
            }
            finally
            {
                external.Close();
                external.Teardown();
            }
        });
    }

    /// <summary>Unchanged crew and objective controls survive status polls while their pressed mouse button is held.</summary>
    [Test]
    public async Task CrewSetupPollingPreservesCrewAndDraftControls()
    {
        await WithCrewWindow(window =>
        {
            var crew = new WGCrewSetupCrew
            {
                Grid = new NetEntity(101), Group = "crew", Members = 3, Alive = 3,
                Objectives = new() { new() { Kind = WGCrewObjectiveKind.Hold, Duration = 10 } },
            };
            UiCall(window, "Receive", UiSnapshot(crew));
            window.SelectCrew(crew.Grid, crew.Group);
            UiCall(window, "BeginQueueEdit");
            var cards = UiField<BoxContainer>(window, "_crewList");
            var queue = UiField<BoxContainer>(window, "_queueRows");
            var card = cards.Children.OfType<ContainerButton>().Single();
            var row = queue.Children.Single();
            var edit = row.Children.OfType<BoxContainer>().Single().Children.OfType<Button>().First();
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            ui.ControlFocused = edit;
            crew.Alive = 2;
            UiCall(window, "Receive", UiSnapshot(crew));
            Assert.Multiple(() =>
            {
                Assert.That(cards.Children.OfType<ContainerButton>().Single(), Is.SameAs(card));
                Assert.That(queue.Children.Single(), Is.SameAs(row));
                Assert.That(ui.ControlFocused, Is.SameAs(edit));
            });
        });
    }

    private static void ArrangeCrewPopup(Popup popup)
    {
        popup.Measure(new Vector2(1000, 700));
        popup.Arrange(UIBox2.FromDimensions(Vector2.Zero, popup.DesiredSize));
    }

    private static void CrewUiKey(IUserInterfaceManager ui, Control control, BoundKeyState state)
    {
        var pointer = new ScreenCoordinates(control.GlobalPixelPosition + control.PixelSize / 2, control.Window?.Id ?? default);
        var args = new BoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, true);
        ui.GetType().GetMethod(state == BoundKeyState.Down ? "KeyBindDown" : "KeyBindUp")!.Invoke(ui, new object[] { args });
    }
}
