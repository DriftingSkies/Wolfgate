#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.Cockpit;
using Content.IntegrationTests.Tests._WG.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._WG.Lattice;
using Content.Shared.Paper;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Owner decisions on reports from the 2026-11-21 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121CallsTest
{
    /// <summary>#1192: the Coda breaks off in writing as it does in speech; the Choir write it plainly.</summary>
    [Test]
    public async Task CodaIsNotWrittenEither()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var writer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var written = WGWrittenTextEvent.Apply(entMan, writer, "They call it the Coda. coda, CODA.");
            Assert.That(written.ToLowerInvariant(), Does.Not.Contain("coda"), "the word was written whole");
            Assert.That(written, Does.StartWith("They call it the "), "the rest of the text is untouched");
            Assert.That(WGWrittenTextEvent.Apply(entMan, writer, "a decoda ring"), Is.EqualTo("a decoda ring"),
                "only the word itself breaks off");

            // Paper takes what the hand managed, not what was typed.
            var paper = entMan.SpawnEntity("Paper", map.GridCoords);
            var message = new PaperComponent.PaperInputTextMessage("The Coda ends it.") { Actor = writer };
            entMan.EventBus.RaiseLocalEvent(paper, message);
            var content = entMan.GetComponent<PaperComponent>(paper).Content;
            Assert.That(content, Does.EndWith(" ends it."));
            Assert.That(content.ToLowerInvariant(), Does.Not.Contain("coda"));

            var choir = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGChoirMemberComponent>(choir);
            Assert.That(WGWrittenTextEvent.Apply(entMan, choir, "the Coda"), Is.EqualTo("the Coda"));

            entMan.DeleteEntity(writer);
            entMan.DeleteEntity(choir);
            entMan.DeleteEntity(paper);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// #1196: a gun bank rebuilt for the same console has the pilot's weapons selected again, and losing the link
    /// does not forget them.
    /// </summary>
    [Test]
    public async Task CockpitRemembersWeaponSelection()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var ui = client.ResolveDependency<IUserInterfaceManager>();
            var memory = client.EntMan.System<WGCockpitGunneryMemorySystem>();
            var console = new NetEntity(990001);
            var entries = Enumerable.Range(1, 4).Select(index => new FireControllableEntry(new NetEntity(990010 + index),
                default, $"Battery {index}", 6, true)).ToArray();
            var state = new FireControlConsoleBoundInterfaceState(true, entries,
                new NavInterfaceState(250, null, null, new(), default));

            var first = new WGCockpitGunneryPanel();
            first.SelectionChanged += () => memory.Remember(console, first.PressedWeapons);
            ui.RootControl.AddChild(first);
            first.UpdateState(state);
            Layout(first);
            WGButtonTestInput.Click(Weapon(first, "Battery 2"));
            WGButtonTestInput.Click(Weapon(first, "Battery 3"));
            var chosen = new[] { entries[1].NetEntity, entries[2].NetEntity };
            Assert.That(memory.Recall(console), Is.EquivalentTo(chosen), "the pilot's selection was not noted");

            // The link drops: the bank clears, which is not the pilot unselecting.
            first.UpdateState(null);
            Assert.That(first.HasSelectedWeapons, Is.False);
            Assert.That(memory.Recall(console), Is.EquivalentTo(chosen), "a lost link wiped the remembered selection");
#pragma warning disable CS0618 // Dispose is how the cockpit tears the bank down.
            first.Dispose();
#pragma warning restore CS0618

            // The helm reopens: a fresh bank for the same console.
            var second = new WGCockpitGunneryPanel();
            ui.RootControl.AddChild(second);
            second.UpdateState(state);
            Assert.That(second.HasSelectedWeapons, Is.False, "a fresh bank starts with nothing selected");
            second.Select(memory.Recall(console));
            Layout(second);
            Assert.That(second.SelectedWeapons, Is.EquivalentTo(chosen), "the selection was not put back");

            // Unselecting everything forgets the console.
            second.SelectionChanged += () => memory.Remember(console, second.PressedWeapons);
            WGButtonTestInput.Click(Weapon(second, "Battery 2"));
            WGButtonTestInput.Click(Weapon(second, "Battery 3"));
            Assert.That(memory.Recall(console), Is.Empty);
#pragma warning disable CS0618
            second.Dispose();
#pragma warning restore CS0618
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Gives the bank a size, as the click helper only presses a button that has been laid out.</summary>
    private static void Layout(Control panel)
    {
        var size = new Vector2(296, 450);
        foreach (var child in WGCockpitLease.Descendants(panel))
            child.InvalidateMeasure();
        panel.Measure(size);
        panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static Button Weapon(Control panel, string name)
    {
        return WGCockpitLease.Descendants(panel).OfType<Button>().Single(button => button.Text == name);
    }
}
