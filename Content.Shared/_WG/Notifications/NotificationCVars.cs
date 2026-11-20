using Robust.Shared.Configuration;

namespace Content.Shared._WG.Notifications;

/// <summary>Which events flash the game window or play an alert sound for the player.</summary>
[CVarDefs]
public sealed class NotificationCVars
{
    /// <summary>Flashes the window when the server asks for attention, such as the round starting.</summary>
    public static readonly CVarDef<bool> FlashRoundStart =
        CVarDef.Create("wg.window_flash_round_start", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Flashes the window on an incoming admin message.</summary>
    public static readonly CVarDef<bool> FlashAHelp =
        CVarDef.Create("wg.window_flash_ahelp", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Flashes the window when a chat message matches one of the player's highlights.</summary>
    public static readonly CVarDef<bool> FlashHighlight =
        CVarDef.Create("wg.window_flash_highlight", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Plays a sound when a chat message matches one of the player's highlights.</summary>
    public static readonly CVarDef<bool> SoundHighlight =
        CVarDef.Create("wg.chat_highlight_sound", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Flashes the window when a new ghost role opens while the player is a ghost.</summary>
    public static readonly CVarDef<bool> FlashGhostRole =
        CVarDef.Create("wg.window_flash_ghost_role", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Plays a sound when a new ghost role opens while the player is a ghost.</summary>
    public static readonly CVarDef<bool> SoundGhostRole =
        CVarDef.Create("wg.ghost_role_sound", false, CVar.ARCHIVE | CVar.CLIENTONLY);
}
