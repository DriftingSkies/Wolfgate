using Robust.Shared.Configuration;

namespace Content.Shared._WG.WornStorage;

/// <summary>Settings for clicking worn storage.</summary>
[CVarDefs]
public sealed class WornStorageCVars
{
    /// <summary>
    /// Left-clicking a worn storage with an empty hand opens it instead of taking it off; dragging it onto an empty
    /// hand takes it off.
    /// </summary>
    public static readonly CVarDef<bool> ClickOpens =
        CVarDef.Create("wg.worn_storage_click_opens", false, CVar.ARCHIVE | CVar.CLIENTONLY);
}
