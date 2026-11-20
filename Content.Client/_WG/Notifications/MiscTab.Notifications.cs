using Content.Shared._WG.Notifications;

namespace Content.Client.Options.UI.Tabs;

/// <summary>The Notifications section of the Misc tab.</summary>
public sealed partial class MiscTab
{
    private void WgAddNotificationOptions()
    {
        Control.AddOptionCheckBox(NotificationCVars.FlashRoundStart, FlashRoundStartCheckBox);
        Control.AddOptionCheckBox(NotificationCVars.FlashAHelp, FlashAHelpCheckBox);
        Control.AddOptionCheckBox(NotificationCVars.FlashHighlight, FlashHighlightCheckBox);
        Control.AddOptionCheckBox(NotificationCVars.SoundHighlight, SoundHighlightCheckBox);
        Control.AddOptionCheckBox(NotificationCVars.FlashGhostRole, FlashGhostRoleCheckBox);
        Control.AddOptionCheckBox(NotificationCVars.SoundGhostRole, SoundGhostRoleCheckBox);
    }
}
