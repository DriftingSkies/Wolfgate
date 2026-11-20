#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Maths;
using Content.Client._WG.CharacterPreview;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WG.CharacterPreview;

/// <summary>
/// The preview frame turns once per drag step, clamps its zoom, lays the sprite out at that zoom, and every floor tile
/// it offers loads.
/// </summary>
[TestFixture]
[TestOf(typeof(WGPreviewFrame))]
public sealed class PreviewFrameTest
{
    [Test]
    public async Task DragZoomAndBackgrounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var frame = new WGPreviewFrame { SetSize = new Vector2(200, 200) };
            // Laid out like the editor's view: a fixed, centred box with a Fit stretch.
            var view = new SpriteView
            {
                Scale = new Vector2(8, 8),
                Margin = new Thickness(4),
                SetSize = new Vector2(192, 192),
                HorizontalAlignment = Control.HAlignment.Center,
                Stretch = SpriteView.StretchMode.Fit,
            };
            frame.AddChild(view);

            var turns = new List<int>();
            frame.Turned += turns.Add;
            frame.Drag(WGPreviewFrame.TurnDistance * 2.5f);
            frame.Drag(-WGPreviewFrame.TurnDistance * 1.5f);
            // 2.5 steps right turns twice and keeps half a step; 1.5 back nets one step left.
            Assert.That(turns, Is.EqualTo(new[] { 1, 1, -1 }), "One turn per full step of drag, either way.");

            frame.SetZoom(10);
            Assert.That(frame.Zoom, Is.EqualTo(WGPreviewFrame.MaxZoom));
            frame.SetZoom(0);
            Assert.That(frame.Zoom, Is.EqualTo(WGPreviewFrame.MinZoom));

            frame.SetZoom(2);
            frame.Measure(new Vector2(200, 200));
            frame.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(200, 200)));
            Assert.That(view.Size.X, Is.EqualTo(384).Within(0.5f), "At 2x the view is laid out twice its fixed size.");
            Assert.That(view.Scale.X, Is.EqualTo(16).Within(0.01f), "At 2x the sprite draws at twice its scale.");
            Assert.That(frame.DesiredSize.X, Is.EqualTo(200).Within(0.5f), "Zooming doesn't grow the frame.");

            frame.SetZoom(1);
            frame.Measure(new Vector2(200, 200));
            frame.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(200, 200)));
            Assert.That(view.Size.X, Is.EqualTo(192).Within(0.5f), "Back at 1x the view returns to its own size.");
            Assert.That(view.Scale.X, Is.EqualTo(8).Within(0.01f));

            for (var i = 0; i <= WGPreviewFrame.Backgrounds.Length; i++)
            {
                Assert.DoesNotThrow(() => frame.SetBackground(i), $"Background {i} should load.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
