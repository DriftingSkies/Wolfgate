using Robust.Shared.Configuration;

namespace Content.Shared._WG.Paintings;

/// <summary>Server settings for paintings and the art catalogue.</summary>
[CVarDefs]
public sealed class WGPaintingCVars
{
    /// <summary>Credits a print ordered from the art app costs. They leave the economy.</summary>
    public static readonly CVarDef<int> OrderPrice =
        CVarDef.Create("wg.paintings.order_price", 500, CVar.SERVERONLY);

    /// <summary>Most paintings one player may scan into the catalogue in a day. 0 for no limit.</summary>
    public static readonly CVarDef<int> DailyScans =
        CVarDef.Create("wg.paintings.daily_scans", 10, CVar.SERVERONLY);

    /// <summary>Catalogue paintings one client may download per minute.</summary>
    public static readonly CVarDef<int> DownloadsPerMinute =
        CVarDef.Create("wg.paintings.downloads_per_minute", 120, CVar.SERVERONLY);
}
