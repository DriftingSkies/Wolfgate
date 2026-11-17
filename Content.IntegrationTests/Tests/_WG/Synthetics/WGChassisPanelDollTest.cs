#nullable enable
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WG.Synthetics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WG.CombatConsole;
using Content.Shared._Shitmed.Targeting;
using Robust.Client.UserInterface.Controls;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.Synthetics;

/// <summary>The chassis panel's body doll, which needs a connected client for its sprite systems.</summary>
[TestFixture]
public sealed class WGChassisPanelDollTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    /// <summary>Clicking a limb on the doll lights its row; clicking a row lights the limb.</summary>
    [Test]
    public async Task DollClicksSelectRowsTest()
    {
        WGChassisPanelWindow? window = null;
        await Client.WaitPost(() =>
        {
            window = new WGChassisPanelWindow();
            window.OpenCentered();
            window.Update(WGChassisPanelWindowTest.State());
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;
            var doll = panel.Doll;
            Assert.That(doll, Is.Not.Null, "a connected client draws the doll.");
            Assert.That(WGChassisPanelWindowTest.Descendants(panel.TabHost.GetChild(0)), Does.Contain(doll),
                "the doll lives in the parts tab.");
            Assert.That(WGChassisPanelWindowTest.Descendants(panel.TabHost.GetChild(1)), Does.Not.Contain(doll));

            int Lit() => WGChassisPanelWindowTest.Descendants(doll!).OfType<TextureRect>().Count(r => r.Visible);
            BaseButton Button(string style) => WGChassisPanelWindowTest.Descendants(doll!).OfType<BaseButton>()
                .Single(b => b.StyleClasses.Contains(style));

            Assert.That(Lit(), Is.EqualTo(0));

            WGButtonTestInput.Click(Button("TargetDollButtonLeftArm"));
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.LeftArm));
            Assert.That(WGChassisPanelWindowTest.Background(panel.Parts[2].Cells[0]),
                Is.EqualTo(WGChassisPanelWindowTest.SelectedBackground));
            Assert.That(Lit(), Is.EqualTo(1));

            WGButtonTestInput.Click(Button("TargetDollButtonGroin"));
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.Torso), "the groin picks the torso row.");

            WGChassisPanelWindowTest.ClickCell(panel.Parts[3].Cells[0]);
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.RightArm));
            Assert.That(Lit(), Is.EqualTo(1));

            // The same limb again clears the pick.
            WGButtonTestInput.Click(Button("TargetDollButtonRightArm"));
            Assert.That(panel.Selected, Is.Null);
            Assert.That(Lit(), Is.EqualTo(0));

            // New states redraw the doll without trouble, and a shorter list is fine.
            var next = WGChassisPanelWindowTest.State();
            next.Parts.RemoveAt(9);
            panel.Update(next);
            panel.Update(WGChassisPanelWindowTest.State(bayOpen: true));
        });

        await Client.WaitPost(() => window!.Teardown());
    }
}
