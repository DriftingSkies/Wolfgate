using Robust.Shared.Configuration;

namespace Content.Shared._WG.CCVar;

/// <summary>
/// Wolfgate settings.
/// </summary>
[CVarDefs]
public sealed class WolfgateCVars
{
    /// <summary>
    /// Which Wolfgate UI skin the client draws. Matches a <c>WolfgateSkin</c> id: "WolfgateRetro", the default, is the
    /// Aphelion cassette-futurism look and "Wolfgate" the hyper-futurist one. Changing it restyles the client
    /// immediately. Keep the default in step with <c>WolfgateSkins.Default</c>.
    /// </summary>
    public static readonly CVarDef<string> UiStyle =
        CVarDef.Create("wg.ui_style", "WolfgateRetro", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Enters the cockpit by itself when the shuttle console is opened while buckled into a seat.
    /// </summary>
    public static readonly CVarDef<bool> CockpitAutoEnter =
        CVarDef.Create("wg.cockpit_auto_enter", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// The MFD page the cockpit last showed, as its page key. Empty or unknown means the NAV page.
    /// </summary>
    public static readonly CVarDef<string> CockpitMfdPage =
        CVarDef.Create("wg.cockpit_mfd_page", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether the cockpit was left with the MFD and the world view swapped.
    /// </summary>
    public static readonly CVarDef<bool> CockpitMfdSwapped =
        CVarDef.Create("wg.cockpit_mfd_swapped", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether the cockpit was left with the MFD expanded to an even split.
    /// </summary>
    public static readonly CVarDef<bool> CockpitMfdExpanded =
        CVarDef.Create("wg.cockpit_mfd_expanded", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether the cockpit was left with the MFD maximised over the world view.
    /// </summary>
    public static readonly CVarDef<bool> CockpitMfdMaximised =
        CVarDef.Create("wg.cockpit_mfd_maximised", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Kill switch for the anatomy system. When false every anatomy gate fails and every anatomy UI is hidden.
    /// </summary>
    public static readonly CVarDef<bool> AnatomyEnabled =
        CVarDef.Create("wg.anatomy_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Version of the adult content notice this client has acknowledged; 0 means never.
    /// </summary>
    public static readonly CVarDef<int> AnatomyNoticeSeen =
        CVarDef.Create("wg.anatomy_notice_seen", 0, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Shows the title bar button that pops in-game windows out into their own OS window.
    /// </summary>
    public static readonly CVarDef<bool> WindowPopout =
        CVarDef.Create("wg.ui_window_popout", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether radar screens label their contacts. Set by the IFF toggle on any radar and shared by all of them.
    /// </summary>
    public static readonly CVarDef<bool> RadarShowIff =
        CVarDef.Create("wg.radar_show_iff", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether radar labels carry their detail lines. Set by the detailed IFF toggle on any radar.
    /// </summary>
    public static readonly CVarDef<bool> RadarShowIffDetailed =
        CVarDef.Create("wg.radar_show_iff_detailed", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// The radar's bearing marks: 0 at the edge, 1 as a ring, 2 off.
    /// </summary>
    public static readonly CVarDef<int> RadarAzimuth =
        CVarDef.Create("wg.radar_azimuth", 0, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether scrolling on a radar zooms toward the cursor instead of the screen centre.
    /// </summary>
    public static readonly CVarDef<bool> RadarZoomToMouse =
        CVarDef.Create("wg.radar_zoom_to_mouse", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// With smart radar tags, every contact on the scope shows its name; the cursor's lens then only adds details.
    /// </summary>
    public static readonly CVarDef<bool> RadarAllNames =
        CVarDef.Create("wg.radar_all_names", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Smart radar tags: quiet by default, every tag and its details round the cursor, overlapping tags stacked. Off,
    /// every tag sits whole by its contact as it always did.
    /// </summary>
    public static readonly CVarDef<bool> RadarSmartTags =
        CVarDef.Create("wg.radar_smart_tags", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Radar contacts show as icons alone: no name or crowd count unless revealed by the cursor or the all-names toggle.</summary>
    public static readonly CVarDef<bool> RadarIconsOnly =
        CVarDef.Create("wg.radar_icons_only", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Hovering the radar reveals tags: the lens round the cursor, or with icons only, the contact under it.</summary>
    public static readonly CVarDef<bool> RadarHoverReveal =
        CVarDef.Create("wg.radar_hover_reveal", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// The player's chat tabs: <c>name=channel,channel</c> entries separated by <c>|</c>, with <c>*</c> for every
    /// channel. Empty means the default tabs.
    /// </summary>
    public static readonly CVarDef<string> ChatTabs =
        CVarDef.Create("wg.chat_tabs", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether saved chat tabs have had the Subtle channel added to an old default Local tab, done once.</summary>
    public static readonly CVarDef<bool> ChatTabsSubtleAdded =
        CVarDef.Create("wg.chat_tabs_subtle_added", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether saved chat tabs that show LOOC have had the SOOC channel added, done once.</summary>
    public static readonly CVarDef<bool> ChatTabsSubtleOocAdded =
        CVarDef.Create("wg.chat_tabs_subtle_ooc_added", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Comma separated entity prototype ids starred in the spawn menu's Favourites folder.
    /// </summary>
    public static readonly CVarDef<string> SpawnFavourites =
        CVarDef.Create("wg.spawn_favourites", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);
}
