using Robust.Shared.Configuration;

namespace Content.Shared._WG.SpeechBubbles;

/// <summary>Which speech bubbles the player sees, and how long they get.</summary>
[CVarDefs]
public sealed class SpeechBubbleCVars
{
    /// <summary>Shows speech bubbles at all. LOOC bubbles keep their own setting.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.speech_bubbles", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Shows bubbles from things that aren't mobs, such as radios and vending machines.</summary>
    public static readonly CVarDef<bool> NonMobs =
        CVarDef.Create("wg.speech_bubbles_non_mobs", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Shows dead chat as bubbles while the player is a ghost.</summary>
    public static readonly CVarDef<bool> Ghosts =
        CVarDef.Create("wg.speech_bubbles_ghosts", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Shows emotes as bubbles.</summary>
    public static readonly CVarDef<bool> Emotes =
        CVarDef.Create("wg.speech_bubbles_emotes", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Cuts a bubble's text to this many characters, with an ellipsis. 0 shows the whole message.</summary>
    public static readonly CVarDef<int> MaxLength =
        CVarDef.Create("wg.speech_bubble_max_length", 0, CVar.ARCHIVE | CVar.CLIENTONLY);
}
