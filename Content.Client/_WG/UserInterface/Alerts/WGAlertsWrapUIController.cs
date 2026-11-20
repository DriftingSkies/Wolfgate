using Content.Client._Shitmed.UserInterface.Systems.Targeting.Widgets;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WG.UserInterface.Alerts;

/// <summary>Wraps the alerts column into more columns before it reaches the targeting doll.</summary>
public sealed class WGAlertsWrapUIController : UIController
{
    private const int Gap = 10;

    private (float Height, float Area)? _applied;

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        // Both screens keep the alerts beside the doll; a borrowed column (cockpit) has another parent.
        if (UIManager.ActiveScreen is not { } screen ||
            screen.GetWidget<AlertsUI>() is not { Parent: LayoutContainer area } alerts ||
            screen.GetWidget<TargetingControl>() is not { } targeting || targeting.Parent != area)
        {
            _applied = null;
            return;
        }

        // A centred column can't grow upward without also growing into the doll.
        if (screen is SeparatedChatGameScreen && alerts.GetValue<float>(LayoutContainer.AnchorTopProperty) != 0)
        {
            LayoutContainer.SetAnchorAndMarginPreset(alerts, LayoutContainer.LayoutPreset.TopRight, margin: Gap);
            LayoutContainer.SetGrowVertical(alerts, LayoutContainer.GrowDirection.End);
        }

        // Window size and alert count change the room under the chat as well as chat resizes.
        if (screen is DefaultGameScreen normal)
            normal.WgPlaceAlerts();

        // The part-status doll and anything else below the grid stays inside the column.
        var below = alerts.DesiredSize.Y - alerts.AlertContainer.DesiredSize.Y;
        var height = MathF.Max(64, targeting.Position.Y - alerts.Position.Y - below - Gap);
        var key = (MathF.Round(height), area.Size.Y);
        if (_applied == key && alerts.AlertContainer.LimitType == LimitType.Size)
            return;
        _applied = key;
        alerts.AlertContainer.MaxGridHeight = height;
    }
}
