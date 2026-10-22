#nullable enable annotations

using System.Numerics;
using Content.Client._WF.UserInterface.Controls;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.UserInterface;

/// <summary>Checks that a scroll box inside a scrolling page hands the wheel back to the page at its edges.</summary>
public sealed class WolfgateNestedScrollContainerTest
{
    [Test]
    public async Task InnerScrollPassesTheWheelOnAtItsEdges()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            using var outer = new ScrollContainer { HScrollEnabled = false };
            var content = new Control { MinSize = new Vector2(300, 400) };
            var inner = new WolfgateNestedScrollContainer
            {
                ReturnMeasure = true,
                MaxHeight = 100,
                Children = { content },
            };
            outer.AddChild(new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Children =
                {
                    new Control { MinSize = new Vector2(300, 150) },
                    inner,
                    new Control { MinSize = new Vector2(300, 300) },
                },
            });
            ui.RootControl.AddChild(outer);
            Layout(outer);
            outer.VScroll = 100;
            Layout(outer);
            var over = new ScreenCoordinates(inner.GlobalPixelPosition + new Vector2(50, 50) * inner.UIScale, inner.Window!.Id);
            Assert.That(ui.MouseGetControl(over), Is.SameAs(inner));

            Wheel(ui, over, 1);
            Assert.That(inner.VScrollTarget, Is.EqualTo(0f), "The inner box is already at its top.");
            Assert.That(outer.VScrollTarget, Is.LessThan(100f), "The page scrolls up while the cursor is over the inner box.");

            outer.VScroll = 100;
            Layout(outer);
            Wheel(ui, over, -1);
            Assert.That(inner.VScrollTarget, Is.GreaterThan(0f), "The inner box scrolls down while it can.");
            Assert.That(outer.VScrollTarget, Is.EqualTo(100f), "And keeps the wheel from the page meanwhile.");

            inner.VScroll = 300;
            Layout(outer);
            Wheel(ui, over, -1);
            Assert.That(outer.VScrollTarget, Is.GreaterThan(100f), "At its bottom the wheel goes on to the page.");

            // A filter shrinks the list while it sits at the bottom: its old scroll target is past the new end.
            outer.VScroll = 100;
            inner.VScroll = 300;
            Layout(outer);
            content.MinSize = new Vector2(300, 200);
            // The frame's layout pass would carry the change up; done by hand here.
            for (Control? control = content; control != null && control != outer.Parent; control = control.Parent)
                control.InvalidateMeasure();
            Layout(outer);
            Assert.That(inner.VScroll, Is.EqualTo(100f), "The list's range shrank.");
            Wheel(ui, over, -1);
            Assert.That(outer.VScrollTarget, Is.GreaterThan(100f), "A list that shrank under the cursor still hands the wheel on at its new end.");
            ui.RootControl.RemoveChild(outer);
        });
        await pair.CleanReturnAsync();
    }

    private static void Layout(Control control)
    {
        control.Measure(new Vector2(300, 200));
        control.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(300, 200)));
    }

    private static void Wheel(IUserInterfaceManager ui, ScreenCoordinates at, float delta)
    {
        ui.GetType().GetMethod("MouseWheel", new[] { typeof(MouseWheelEventArgs) })!
            .Invoke(ui, new object[] { new MouseWheelEventArgs(new Vector2(0, delta), at) });
    }
}
