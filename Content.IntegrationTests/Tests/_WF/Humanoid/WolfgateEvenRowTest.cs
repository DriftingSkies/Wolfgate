#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Humanoid;
using Content.Client._WF.UserInterface.Controls;
using Content.Shared.Humanoid.Markings;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Humanoid;

/// <summary>
/// The creator's hair and facial hair pickers share their card evenly, and neither's tiles reach past its half, at any
/// width: a grid had the hair picker take columns for the whole card, cutting its tiles off or pushing facial hair out.
/// </summary>
public sealed class WolfgateEvenRowTest
{
    [TestCase(1400f)]
    [TestCase(1150f)]
    [TestCase(700f)]
    public async Task PickersShareTheRowAndFitTheirHalf(float width)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var hair = new WolfgateSlotMarkingPicker { Category = MarkingCategories.Hair, HorizontalExpand = true };
            var beard = new WolfgateSlotMarkingPicker { Category = MarkingCategories.FacialHair, HorizontalExpand = true };
            hair.UpdateData(new List<Marking>(), "Human", 1);
            beard.UpdateData(new List<Marking>(), "Human", 1);
            var row = new WolfgateEvenRow { Separation = 16, Children = { hair, beard } };

            row.Measure(new Vector2(width, 600));
            row.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(width, 600)));

            var half = (width - 16) / 2;
            Assert.Multiple(() =>
            {
                Assert.That(hair.Width, Is.EqualTo(half).Within(1), "hair gets half the row");
                Assert.That(beard.Width, Is.EqualTo(half).Within(1), "facial hair gets the other half");
                Assert.That(beard.Position.X + beard.Width, Is.LessThanOrEqualTo(width + 0.5f), "facial hair stays in the row");
                foreach (var picker in new[] { hair, beard })
                {
                    var grid = Descendants(picker).OfType<WolfgateNestedScrollContainer>().First().Children.OfType<GridContainer>().First();
                    Assert.That(grid.DesiredSize.X, Is.LessThanOrEqualTo(picker.Width + 0.5f),
                        $"{picker.Category} asks for {grid.DesiredSize.X} of tiles in {picker.Width}");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
