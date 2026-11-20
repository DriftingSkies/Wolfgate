using Robust.Shared.Configuration;

namespace Content.Shared._WG.Screentips;

/// <summary>Where screentips show, if at all.</summary>
public enum ScreentipMode
{
    Off = 0,

    /// <summary>Centred at the top of the game view.</summary>
    TopOfView = 1,

    /// <summary>Beside the mouse pointer.</summary>
    NextToCursor = 2,
}

/// <summary>Settings for screentips, the name of whatever is under the cursor.</summary>
[CVarDefs]
public sealed class ScreentipCVars
{
    /// <summary>A <see cref="ScreentipMode"/>: off, at the top of the game view, or next to the cursor.</summary>
    public static readonly CVarDef<int> Mode =
        CVarDef.Create("wg.screentips", (int) ScreentipMode.NextToCursor, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>The screentip's text colour, as hex.</summary>
    public static readonly CVarDef<string> Color =
        CVarDef.Create("wg.screentip_color", "#ffffff", CVar.ARCHIVE | CVar.CLIENTONLY);
}
