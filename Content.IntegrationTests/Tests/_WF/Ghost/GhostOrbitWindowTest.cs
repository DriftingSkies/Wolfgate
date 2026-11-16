#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client._WF.Ghost;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Ghost;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Ghost;

public sealed class GhostOrbitWindowTest : InteractionTest
{
    /// <summary>A click on a section's header folds it, another opens it again, and a refresh keeps it as it was left.</summary>
    [Test]
    public async Task SectionsFoldAndStayFolded()
    {
        GhostOrbitWindow window = default!;
        var receive = typeof(GhostOrbitWindow).GetMethod("OnTargetsReceived", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<GhostOrbitTarget> Targets(int followers) => new()
        {
            new GhostOrbitTarget { Entity = Player, Name = "Somebody", Category = GhostOrbitCategory.Alive, Followers = followers },
            new GhostOrbitTarget { Entity = Player, Name = "A ship", Category = GhostOrbitCategory.Ship },
        };

        await Client.WaitPost(() =>
        {
            window = new GhostOrbitWindow();
            window.OpenCentered();
            receive.Invoke(window, new object[] { new GhostOrbitTargetsEvent(Targets(0)) });
        });
        await RunTicks(5);

        (ContainerButton Header, Control Body) Section(int index)
        {
            var root = window.FindControl<BoxContainer>("Sections").GetChild(index);
            return ((ContainerButton) root.GetChild(0), root.GetChild(1));
        }

        await Client.WaitAssertion(() => Assert.That(Section(0).Body.Visible, Is.True));
        await ClickControl(Section(0).Header);
        await RunTicks(2);
        await Client.WaitAssertion(() => Assert.That(Section(0).Body.Visible, Is.False, "A click folds the section."));

        await Client.WaitPost(() => receive.Invoke(window, new object[] { new GhostOrbitTargetsEvent(Targets(1)) }));
        await RunTicks(2);
        await Client.WaitAssertion(() => Assert.That(Section(0).Body.Visible, Is.False, "A refresh leaves it folded."));

        await ClickControl(Section(0).Header);
        await RunTicks(2);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Section(0).Body.Visible, Is.True, "Another click opens it.");
            window.Close();
        });
    }

    /// <summary>
    /// The Faction view puts players and ships under their faction, the unaligned last; the Place view puts everything
    /// under where it is, the sector first.
    /// </summary>
    [Test]
    public async Task ViewsGroupByFactionAndByPlace()
    {
        GhostOrbitWindow window = default!;
        var receive = typeof(GhostOrbitWindow).GetMethod("OnTargetsReceived", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var targets = new List<GhostOrbitTarget>
        {
            new() { Entity = Player, Name = "Free trader", Category = GhostOrbitCategory.Alive, Place = "Asclepiu", PlaceOrder = 1 },
            new() { Entity = Player, Name = "Marine", Category = GhostOrbitCategory.Alive, Faction = "Trans-Solarian Federation", Place = "Sector" },
            new() { Entity = Player, Name = "Frigate", Category = GhostOrbitCategory.Ship, Faction = "Trans-Solarian Federation", Place = "Sector" },
        };

        List<string> Titles() => window.FindControl<BoxContainer>("Sections").Children
            .Select(root => ((Label) ((BoxContainer) ((ContainerButton) root.GetChild(0)).GetChild(0)).GetChild(1)).Text ?? string.Empty)
            .ToList();

        await Client.WaitPost(() =>
        {
            window = new GhostOrbitWindow();
            window.OpenCentered();
            window.FindControl<Button>("FactionViewButton").Pressed = true;
            typeof(GhostOrbitWindow).GetMethod("SetView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { false });
            receive.Invoke(window, new object[] { new GhostOrbitTargetsEvent(targets) });
        });
        await RunTicks(2);

        await Client.WaitAssertion(() =>
        {
            var titles = Titles();
            Assert.That(titles, Has.Count.EqualTo(2), string.Join(", ", titles));
            Assert.That(titles[0], Does.Contain("Trans-Solarian Federation"), "A faction comes before the unaligned.");
            Assert.That(titles[1], Does.Contain("Independent"));

            typeof(GhostOrbitWindow).GetMethod("SetView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { true });
            titles = Titles();
            Assert.That(titles, Has.Count.EqualTo(2), string.Join(", ", titles));
            Assert.That(titles[0], Does.Contain("Sector"), "The sector comes before the worlds.");
            Assert.That(titles[1], Does.Contain("Asclepiu"));

            // Leave the session's view as players start with it.
            typeof(GhostOrbitWindow).GetMethod("SetView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { false });
            window.Close();
        });
    }
}
