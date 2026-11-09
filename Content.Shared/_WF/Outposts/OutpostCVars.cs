using Robust.Shared.Configuration;

namespace Content.Shared._WF.Outposts;

/// <summary>Settings for player outposts.</summary>
[CVarDefs]
public sealed class OutpostCVars
{
    /// <summary>Whether outposts can be founded.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.outposts.enabled", true, CVar.SERVERONLY);

    /// <summary>Most floor tiles one outpost may have; foundation plates are refused past it.</summary>
    public static readonly CVarDef<int> MaxTiles =
        CVarDef.Create("wf.outposts.max_tiles", 1600, CVar.SERVERONLY);

    /// <summary>Widest an outpost's floor may spread across either axis, in tiles; plates past it are refused.</summary>
    public static readonly CVarDef<int> MaxSpan =
        CVarDef.Create("wf.outposts.max_span", 64, CVar.SERVERONLY);

    /// <summary>
    /// Most entities one outpost may hold and still be saved: a save takes about 0.21 ms an entity on the main thread
    /// (spike F0.2), so this keeps it near half a second.
    /// </summary>
    public static readonly CVarDef<int> MaxEntities =
        CVarDef.Create("wf.outposts.max_entities", 2000, CVar.SERVERONLY);

    /// <summary>Seconds a save's outline shows on the ground before the save lands.</summary>
    public static readonly CVarDef<float> LoadDelay =
        CVarDef.Create("wf.outposts.load_delay", 30f, CVar.SERVERONLY);

    /// <summary>Seconds between autosaves of an outpost that changed, into the slot it was last saved into or loaded from.</summary>
    public static readonly CVarDef<float> AutosaveInterval =
        CVarDef.Create("wf.outposts.autosave_interval", 600f, CVar.SERVERONLY);

    /// <summary>Whether a Trade tab order needs the world's planetary comms up (PlanetComms) to reach the outfitter.</summary>
    public static readonly CVarDef<bool> TradeNeedsComms =
        CVarDef.Create("wf.outposts.trade_needs_comms", true, CVar.SERVERONLY);

    /// <summary>Seconds from paying for a Trade tab order to its crate dropping out of the sky.</summary>
    public static readonly CVarDef<float> TradeLeadTime =
        CVarDef.Create("wf.outposts.trade_lead_time", 30f, CVar.SERVERONLY);

    /// <summary>Most outposts live at once; Outpost crews past it fall back to a normal start.</summary>
    public static readonly CVarDef<int> MaxLive =
        CVarDef.Create("wf.outposts.max_live", 40, CVar.SERVERONLY);
}
