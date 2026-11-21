using Content.Client.Options.UI;
using Content.Shared._WG.Notifications;

namespace Content.Client._WG.Options.UI.Tabs;

/// <summary>The Notifications section of the Wolfgate Chat tab.</summary>
public sealed partial class WGChatTab
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
