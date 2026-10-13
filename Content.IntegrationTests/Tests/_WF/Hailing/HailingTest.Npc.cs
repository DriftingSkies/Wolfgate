#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Hailing;
using Content.Server._WF.Hailing;
using Content.Shared._WF.Hailing;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.Hailing;

/// <summary>
/// NPC ends of a hail: automated consoles that need no UI or player, and the answers one end can put in
/// front of the other.
/// </summary>
public sealed partial class HailingTest
{
    [Test]
    public async Task AutomatedEndsHoldACallWithNoUi()
    {
        // Test pairs run without PVS, which would send the NPC regardless; TestPair reverts this.
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CVars.NetPVS, true));
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var ui = SEntMan.System<SharedUserInterfaceSystem>();
        var npc = SEntMan.GetNetEntity(_otherPilot);
        var otherShip = string.Empty;
        await Server.WaitPost(() => otherShip = SEntMan.GetComponent<MetaDataComponent>(_otherGrid).EntityName);

        // Nobody is at the other console and no session is behind its pilot.
        await Server.WaitPost(() => ui.CloseUi(_otherConsole, ShuttleConsoleUiKey.Key, _otherPilot));
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(_otherConsole, ShuttleConsoleUiKey.Key, _otherPilot), Is.False);
            Assert.That(SEntMan.HasComponent<ActorComponent>(_otherPilot), Is.False);

            // An ordinary hail needs somebody at the console.
            Assert.That(hailing.TryHail(_otherConsole, _otherPilot, MapData.Grid), Is.True);
        });

        await RunSeconds(1.2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(hailing.GetState(_otherConsole), Is.Null, "A plain hail from an unmanned console should drop.");
            Assert.That(hailing.TryHailAutomated(_otherConsole, SPlayer, MapData.Grid), Is.False,
                "An operator who is on another ship can't man the console.");
        });

        await RunSeconds(2.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(hailing.TryHailAutomated(_otherConsole, _otherPilot, MapData.Grid), Is.True);
            Assert.That(hailing.GetState(_otherConsole), Is.EqualTo(HailState.Calling));
            Assert.That(hailing.GetState(_console), Is.EqualTo(HailState.Ringing));
            hailing.Answer(_console, SPlayer, true);
            Assert.That(hailing.GetState(_otherConsole), Is.EqualTo(HailState.Connected));
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(_otherConsole).Automated, Is.True);
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(_console).PeerOperator, Is.EqualTo(npc));
        });

        await RunSeconds(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(hailing.GetState(_otherConsole), Is.EqualTo(HailState.Connected), "The call should outlast the check.");
            Assert.That(hailing.GetState(_console), Is.EqualTo(HailState.Connected));
            Assert.That(hailing.Say(_otherConsole, _otherPilot, "Heave to."), Is.True);
        });

        await RunTicks(15);

        await Client.WaitAssertion(() =>
        {
            var console = CEntMan.GetEntity(SEntMan.GetNetEntity(_console));
            var hail = CEntMan.GetComponent<HailConsoleComponent>(console);
            Assert.That(hail.Log.Select(l => (l.Text, l.Own)), Is.EqualTo(new[] { ("Heave to.", false) }));
            Assert.That(CEntMan.TryGetEntity(npc, out _), "The player should be sent an NPC with no session for the screen.");
            Assert.That(FindHailWindow()!.FindControl<Label>("StatusLabel").Text,
                Is.EqualTo(Loc.GetString("hail-banner-open", ("ship", otherShip))));
        });

        // The operator dying drops the call.
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(_otherPilot, MobState.Dead));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(hailing.GetState(_otherConsole), Is.Null, "A call should end when its NPC dies.");
            Assert.That(hailing.GetState(_console), Is.Null);
        });
    }

    [Test]
    public async Task AutomatedRingNobodyAnswersEnds()
    {
        await SetupShips();
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var timeout = cfg.GetCVar(HailingCVars.RingTimeout);

        try
        {
            await Server.WaitPost(() => Server.ResolveDependency<IConsoleHost>().ExecuteCommand(ServerSession, "hailtest"));
            await RunSeconds(3.5f);

            await Server.WaitAssertion(() =>
            {
                Assert.That(State(_console), Is.EqualTo(HailState.Ringing), "The test pilot should be hailing.");
                Assert.That(TestShip().Ended, Is.Zero);
                cfg.SetCVar(HailingCVars.RingTimeout, 1f);
            });

            await RunSeconds(3f);

            await Server.WaitAssertion(() =>
            {
                var ship = TestShip();
                Assert.That(State(_console), Is.Null);
                Assert.That(ship.Ended, Is.EqualTo(1), "The ended event should be raised once.");
                Assert.That(ship.LastEnd, Is.EqualTo(HailEndReason.NoAnswer));
                Assert.That(ship.LastEndConnected, Is.False);
                Assert.That(ship.Answered, Is.Zero);
            });
        }
        finally
        {
            await Server.WaitPost(() => cfg.SetCVar(HailingCVars.RingTimeout, timeout));
        }
    }

    [Test]
    public async Task AutomatedCallerIsDeclinedAndAnswered()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var console = Server.ResolveDependency<IConsoleHost>();

        await Server.WaitPost(() => console.ExecuteCommand(ServerSession, "hailtest"));
        await RunSeconds(3.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_console), Is.EqualTo(HailState.Ringing));
            hailing.Answer(_console, SPlayer, false);
        });

        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var ship = TestShip();
            Assert.That(ship.Ended, Is.EqualTo(1));
            Assert.That(ship.LastEnd, Is.EqualTo(HailEndReason.Declined));
            Assert.That(ship.LastEndConnected, Is.False);
            Assert.That(ship.Answered, Is.Zero);
        });

        // A second hail, answered this time: the caller hears of it, and the call holds with no UI open.
        await Server.WaitPost(() => console.ExecuteCommand(ServerSession, "hailtest"));
        await RunSeconds(3.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_console), Is.EqualTo(HailState.Ringing));
            hailing.Answer(_console, SPlayer, true);
            Assert.That(TestShip().Answered, Is.EqualTo(1));
        });

        await RunSeconds(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_console), Is.EqualTo(HailState.Connected));
            var heard = SEntMan.GetComponent<HailConsoleComponent>(_console).Log.Where(l => !l.Own).Select(l => l.Text);
            Assert.That(heard, Does.Contain(Loc.GetString("hail-test-greeting")));
            hailing.End(_console);
        });

        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var ship = TestShip();
            Assert.That(ship.Ended, Is.EqualTo(2));
            Assert.That(ship.LastEnd, Is.EqualTo(HailEndReason.PeerClosed));
            Assert.That(ship.LastEndConnected, Is.True);
        });
    }

    [Test]
    public async Task OffersCanBePickedButNotTypedOver()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var console = Server.ResolveDependency<IConsoleHost>();

        await Server.WaitPost(() => console.ExecuteCommand(ServerSession, "hailtest options"));
        await RunSeconds(3.5f);
        await Server.WaitPost(() => hailing.Answer(_console, SPlayer, true));
        await RunSeconds(1.5f);

        var prompt = string.Empty;
        var picked = string.Empty;
        var logged = 0;
        await Server.WaitPost(() =>
        {
            prompt = Loc.GetString("hail-test-options-prompt");
            picked = Loc.GetString("hail-test-option-b");
        });

        await Server.WaitAssertion(() =>
        {
            var hail = SEntMan.GetComponent<HailConsoleComponent>(_console);
            Assert.That(hail.Options.Select(o => o.Id), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(hail.Log.Select(l => l.Text), Does.Contain(prompt));
            logged = hail.Log.Count;
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(TestShip().Console).Options, Is.Empty,
                "The answers go to the other end, not the offerer.");
        });

        await RunTicks(15);

        UIBox2 window = default;

        await Client.WaitAssertion(() =>
        {
            var hail = CEntMan.GetComponent<HailConsoleComponent>(CEntMan.GetEntity(SEntMan.GetNetEntity(_console)));
            Assert.That(hail.Options, Has.Count.EqualTo(3), "The offer should reach the client.");

            var win = FindHailWindow()!;
            Assert.That(win.FindControl<LineEdit>("ChatEntry").VisibleInTree, Is.False, "The answers replace the chat line.");
            Assert.That(win.FindControl<Control>("TypeRow").Visible, Is.False);
            Assert.That(win.FindControl<Control>("ChatRow").VisibleInTree);
            Assert.That(win.FindControl<OutputPanel>("ChatLog").VisibleInTree, Is.True, "The log stays.");

            var buttons = win.FindControl<BoxContainer>("OptionRow").Children.OfType<Button>().ToList();
            Assert.That(buttons.Select(b => b.Text), Is.EqualTo(new[]
            {
                Loc.GetString("hail-test-option-a"), picked, Loc.GetString("hail-test-option-c"),
            }));
            Assert.That(buttons.All(b => b.VisibleInTree && !b.Disabled), "The pilot can press them.");
            Assert.That(Helm().FindControl<HailKey>("HailButton").Text, Is.EqualTo(Loc.GetString("hail-key-reply")));
            window = Box(win);
        });

        await Server.WaitAssertion(() =>
        {
            var hail = SEntMan.GetComponent<HailConsoleComponent>(_console);
            Assert.That(hailing.Say(_console, SPlayer, "typed over"), Is.False, "No typing while answers are showing.");
            Raise(_console, new HailSayMessage("typed over"), SPlayer);
            Raise(_console, new HailChooseMessage("nope"), SPlayer);
            Raise(_console, new HailChooseMessage("b"), _otherPilot);
            Assert.That(hailing.Choose(_console, _otherPilot, "b"), Is.False, "Only the pilot on the call may choose.");
            Assert.That(hail.Options, Has.Count.EqualTo(3), "A bad choice leaves the offer up.");
            Assert.That(hail.Log, Has.Count.EqualTo(logged));
            Assert.That(TestShip().Chosen, Is.Zero);

            Raise(_console, new HailChooseMessage("b"), SPlayer);
            Assert.That(hail.Options, Is.Empty, "A choice takes the offer down.");
            Assert.That(TestShip().Chosen, Is.EqualTo(1));
            Assert.That(hail.Log.Last().Text, Is.EqualTo(picked));
            Assert.That(hail.Log.Last().Own, Is.True, "It reads as said by the pilot who chose.");

            Raise(_console, new HailChooseMessage("b"), SPlayer);
            Assert.That(TestShip().Chosen, Is.EqualTo(1), "The same choice can't be taken twice.");
        });

        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            var heard = SEntMan.GetComponent<HailConsoleComponent>(_console).Log.Where(l => !l.Own).Select(l => l.Text).ToList();
            Assert.That(heard, Does.Contain(Loc.GetString("hail-test-picked", ("choice", picked))), "The test pilot should say what was picked.");
            Assert.That(heard, Does.Not.Contain(Loc.GetString("hail-test-reply", ("text", picked))), "A picked answer is not echoed.");
            Assert.That(TestShip().Chosen, Is.EqualTo(1));
            Assert.That(hailing.Say(_console, SPlayer, "ping"), Is.True, "Typing is back once the offer is taken.");
        });

        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            var heard = SEntMan.GetComponent<HailConsoleComponent>(_console).Log.Where(l => !l.Own).Select(l => l.Text);
            Assert.That(heard, Does.Contain(Loc.GetString("hail-test-reply", ("text", "ping"))));
        });

        await Client.WaitAssertion(() =>
        {
            var win = FindHailWindow()!;
            Assert.That(win.FindControl<LineEdit>("ChatEntry").VisibleInTree, "The chat line should come back.");
            Assert.That(win.FindControl<Control>("OptionRow").Visible, Is.False);
            Assert.That(win.FindControl<Control>("OptionRow").ChildCount, Is.Zero);
            Assert.That(Box(win), Is.EqualTo(window), "The window must not change size with the answers.");
            Assert.That(Helm().FindControl<HailKey>("HailButton").Text, Is.EqualTo(Loc.GetString("hail-key-open")));
        });
    }

    [Test]
    public async Task OfferRules()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var one = new List<HailOption> { new("x", "Yes") };

        await Server.WaitAssertion(() =>
            Assert.That(hailing.Offer(_otherConsole, _otherPilot, one), Is.False, "There is nobody to offer to yet."));

        await Connect(hailing);

        await Server.WaitAssertion(() =>
        {
            var hail = SEntMan.GetComponent<HailConsoleComponent>(_console);
            var tooMany = Enumerable.Range(0, HailConsoleComponent.MaxOptions + 1).Select(i => new HailOption("o" + i, "Option")).ToList();
            var longText = new string('z', HailConsoleComponent.MaxOptionLength + 20);

            Assert.That(hailing.Offer(_otherConsole, SPlayer, one), Is.False, "Only the pilot on the call can offer.");
            Assert.That(hailing.Offer(_otherConsole, _otherPilot, new List<HailOption>()), Is.False);
            Assert.That(hailing.Offer(_otherConsole, _otherPilot, tooMany), Is.False);
            Assert.That(hailing.Offer(_otherConsole, _otherPilot, new List<HailOption> { new("x", "A"), new("x", "B") }), Is.False);
            Assert.That(hailing.Offer(_otherConsole, _otherPilot, new List<HailOption> { new("", "A") }), Is.False);
            Assert.That(hailing.Offer(_otherConsole, _otherPilot, new List<HailOption> { new("x", "  \n ") }), Is.False,
                "An answer with nothing to read is no answer.");
            Assert.That(hail.Options, Is.Empty);

            Assert.That(hailing.Offer(_otherConsole, _otherPilot, new List<HailOption> { new("x", longText), new("y", "No") }), Is.True);
            Assert.That(hail.Options, Has.Count.EqualTo(2));
            Assert.That(hail.Options[0].Text, Has.Length.EqualTo(HailConsoleComponent.MaxOptionLength));
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(_otherConsole).Options, Is.Empty);

            hailing.ClearOptions(_otherConsole);
            Assert.That(hail.Options, Is.Empty, "The offerer can take its answers back.");
            Assert.That(hailing.Say(_console, SPlayer, "Back to typing."), Is.True);

            Assert.That(hailing.Offer(_otherConsole, _otherPilot, one), Is.True);
            hailing.End(_otherConsole);
            Assert.That(SEntMan.HasComponent<HailConsoleComponent>(_console), Is.False, "The offer goes with the call.");
        });
    }

    /// <summary>The ship <c>hailtest</c> brought in.</summary>
    private HailTestShipComponent TestShip()
    {
        var ships = SEntMan.AllEntityQueryEnumerator<HailTestShipComponent>();
        Assert.That(ships.MoveNext(out _, out var ship), "The command should bring in a test ship.");
        return ship!;
    }
}
