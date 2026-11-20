using Robust.Shared.Configuration;

namespace Content.Shared._WG.HideTyping;

/// <summary>Settings for the player's own typing indicator.</summary>
[CVarDefs]
public sealed class HideTypingCVars
{
    /// <summary>
    /// Shows others a typing indicator while this player types. The server's <c>chat.show_typing_indicator</c> can
    /// still turn indicators off for everyone.
    /// </summary>
    public static readonly CVarDef<bool> ShowMyTyping =
        CVarDef.Create("wg.chat_show_my_typing", true, CVar.ARCHIVE | CVar.CLIENTONLY);
}
