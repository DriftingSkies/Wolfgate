namespace Content.Client.UserInterface.Screens;

public sealed partial class DefaultGameScreen
{
    private const float WgAlertsGap = 10;
    private const float WgAlertCell = 64;

    private (float Left, float Right)? _wgAlertsUnderChat;

    /// <summary>Puts the alerts under the chat, or to its left when the chat leaves no room for them below.</summary>
    public void WgPlaceAlerts()
    {
        // A borrowed column (cockpit) is placed by its new parent.
        if (Alerts.Parent != this)
            return;

        // The preset's margins, kept so the column can return under the chat.
        _wgAlertsUnderChat ??= (Alerts.GetValue<float>(MarginLeftProperty), Alerts.GetValue<float>(MarginRightProperty));

        var chatTop = Chat.GetValue<float>(MarginTopProperty);
        var chatBottom = Chat.GetValue<float>(MarginBottomProperty);
        if (Size.Y <= 0 || WgAlertsFitUnder(chatBottom))
        {
            var (left, right) = _wgAlertsUnderChat.Value;
            WgSetAlertsMargins(left, chatBottom, right);
        }
        else
        {
            // Zero width at the chat's left edge; the column grows leftward to its own size.
            var edge = Chat.GetValue<float>(MarginLeftProperty) - WgAlertsGap;
            WgSetAlertsMargins(edge, chatTop, edge);
        }
    }

    /// <summary>Whether the doll and every alert fit between the chat and the targeting doll, no wider than the chat.</summary>
    private bool WgAlertsFitUnder(float chatBottom)
    {
        var grid = Alerts.AlertContainer;
        var doll = Alerts.DesiredSize.Y - grid.DesiredSize.Y;
        var columns = Math.Max(1, (int) (Chat.Size.X / WgAlertCell));
        var rows = Math.Max(1, (grid.ChildCount + columns - 1) / columns);
        var floor = Targeting.Visible ? Targeting.Position.Y : Size.Y;
        var room = floor - chatBottom - WgAlertsGap - doll;
        return room >= rows * WgAlertCell;
    }

    private void WgSetAlertsMargins(float left, float top, float right)
    {
        // Margin setters relayout even when unchanged, and this runs every frame.
        if (Alerts.GetValue<float>(MarginLeftProperty) != left)
            SetMarginLeft(Alerts, left);
        if (Alerts.GetValue<float>(MarginRightProperty) != right)
            SetMarginRight(Alerts, right);
        if (Alerts.GetValue<float>(MarginTopProperty) != top)
            SetMarginTop(Alerts, top);
    }
}
