using Robust.Shared.Configuration;

namespace Content.Shared._WG.CharacterPreview;

/// <summary>Settings for the character editor's preview.</summary>
[CVarDefs]
public sealed class CharacterPreviewCVars
{
    /// <summary>Which floor tile the preview character stands on; 0 is none. See <c>WGPreviewFrame.Backgrounds</c>.</summary>
    public static readonly CVarDef<int> Background =
        CVarDef.Create("wg.preview_background", 0, CVar.ARCHIVE | CVar.CLIENTONLY);
}
