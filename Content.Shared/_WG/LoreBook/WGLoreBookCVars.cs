using Robust.Shared.Configuration;

namespace Content.Shared._WG.LoreBook;

/// <summary>Settings for the lore book.</summary>
[CVarDefs]
public sealed class WGLoreBookCVars
{
    /// <summary>The last edition of the book this player has been shown; a newer one opens the book for them once.</summary>
    public static readonly CVarDef<int> SeenEdition =
        CVarDef.Create("wg.lorebook.seen_edition", 0, CVar.CLIENTONLY | CVar.ARCHIVE);
}
