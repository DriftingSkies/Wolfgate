using System.Numerics;
using Content.Shared._WG.RoundSetup;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.RoundSetup;

/// <summary>An alert-sized compass in the status column pointing to the crew leader.</summary>
public sealed class WGCrewLocatorHud : PanelContainer
{
    private static readonly Color Tracking = Color.FromHex("#70DCC8");
    private static readonly Color Down = Color.FromHex("#FF786C");

    private readonly CrewArrow _arrow = new();
    private readonly Label _label = new()
    {
        HorizontalAlignment = HAlignment.Center,
        VerticalAlignment = VAlignment.Bottom,
        MouseFilter = MouseFilterMode.Ignore,
    };

    public WGCrewLocatorHud()
    {
        Name = "CrewLocator";
        SetSize = new Vector2(64, 64);
        HorizontalAlignment = HAlignment.Left;
        MouseFilter = MouseFilterMode.Stop;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#0E1B20CC"), BorderColor = Color.FromHex("#2E4A50"),
            BorderThickness = new Thickness(2), ContentMarginBottomOverride = 1,
        };
        _label.Text = Loc.GetString("wg-round-setup-locator-label");
        AddChild(_arrow);
        AddChild(_label);
    }

    /// <summary>Updates the arrow, colour and tooltip without rebuilding the control.</summary>
    public void Refresh(WGCrewSignalEvent signal, Vector2? direction)
    {
        var name = Loc.GetString("wg-round-setup-locator-leader", ("name", signal.Leader));
        var status = signal.Status switch
        {
            WGCrewLeaderStatus.Self => "wg-round-setup-locator-self",
            WGCrewLeaderStatus.Down => "wg-round-setup-locator-down",
            WGCrewLeaderStatus.Elsewhere => "wg-round-setup-locator-elsewhere",
            WGCrewLeaderStatus.Tracking => null,
            _ => "wg-round-setup-locator-unavailable",
        };
        var radio = Loc.GetString("wg-round-setup-locator-radio");
        ToolTip = status == null ? $"{name}\n{radio}" : $"{name}\n{Loc.GetString(status)}\n{radio}";
        _arrow.Direction = direction;
        var color = signal.Status == WGCrewLeaderStatus.Down ? Down : Tracking;
        _arrow.Modulate = color;
        _label.FontColorOverride = color;
    }

    private sealed class CrewArrow : Control
    {
        public Vector2? Direction;

        public CrewArrow()
        {
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var center = new Vector2(PixelSize.X / 2f, PixelSize.Y * 0.4f);
            var scale = PixelSize.Y / 64f;
            if (Direction is not { } direction || direction.LengthSquared() < 4)
            {
                handle.DrawCircle(center, 5 * scale, Color.White);
                return;
            }
            var forward = Vector2.Normalize(direction);
            var side = new Vector2(-forward.Y, forward.X);
            var points = new[] { center + forward * 16 * scale,
                center - forward * 10 * scale + side * 11 * scale,
                center - forward * 4 * scale,
                center - forward * 10 * scale - side * 11 * scale };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, points, Color.White);
        }
    }
}
