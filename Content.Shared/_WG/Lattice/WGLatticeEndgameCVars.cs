using Robust.Shared.Configuration;

namespace Content.Shared._WG.Lattice;

/// <summary>The Lattice's end: the Unstringing when the last primary anchor falls, and Tacet after it.</summary>
[CVarDefs]
public sealed class WGLatticeEndgameCVars
{
    /// <summary>
    /// Seconds a lost anchor's hold takes to fade to nothing, so the bands creep inward rather than jump; at the Unstringing
    /// every relay and station anchor fades the same way. 0 lets go at once.
    /// </summary>
    public static readonly CVarDef<float> AnchorFade =
        CVarDef.Create("wg.lattice.anchor_fade", 1000f, CVar.SERVERONLY);

    /// <summary>Whether Kyphrus implodes some time after the Unstringing.</summary>
    public static readonly CVarDef<bool> Tacet =
        CVarDef.Create("wg.lattice.tacet", true, CVar.SERVERONLY);

    /// <summary>Seconds after the Coda reaches the centre that Kyphrus implodes.</summary>
    public static readonly CVarDef<float> TacetCentreDelay =
        CVarDef.Create("wg.lattice.tacet_centre_delay", 15f, CVar.SERVERONLY);

    /// <summary>
    /// Soonest and latest Tacet comes after the Unstringing has fully set in, in seconds, should the Coda never reach the
    /// centre (a haven parked on it).
    /// </summary>
    public static readonly CVarDef<float> TacetMin =
        CVarDef.Create("wg.lattice.tacet_min", 900f, CVar.SERVERONLY);

    public static readonly CVarDef<float> TacetMax =
        CVarDef.Create("wg.lattice.tacet_max", 1800f, CVar.SERVERONLY);

    /// <summary>After Tacet, how often working bulbs are rolled to burst, in seconds, and the chance each one does.</summary>
    public static readonly CVarDef<float> DarkInterval =
        CVarDef.Create("wg.lattice.tacet_dark_interval", 30f, CVar.SERVERONLY);

    public static readonly CVarDef<float> DarkChance =
        CVarDef.Create("wg.lattice.tacet_dark_chance", 0.25f, CVar.SERVERONLY);

    /// <summary>Chance each shockwave of Kyphrus's burst bursts a bulb it passes.</summary>
    public static readonly CVarDef<float> WaveBulbChance =
        CVarDef.Create("wg.lattice.tacet_wave_bulb_chance", 0.55f, CVar.SERVERONLY);

    /// <summary>Speed a shockwave knocks a ship outward, in metres a second, and how hard it spins it, in radians a second.</summary>
    public static readonly CVarDef<float> WaveShove =
        CVarDef.Create("wg.lattice.tacet_wave_shove", 18f, CVar.SERVERONLY);

    public static readonly CVarDef<float> WaveSpin =
        CVarDef.Create("wg.lattice.tacet_wave_spin", 1.2f, CVar.SERVERONLY);

    /// <summary>After the Unstringing, seconds for a mobile relay's power draw to double; it keeps doubling while it stands.</summary>
    public static readonly CVarDef<float> HavenLoadDoubling =
        CVarDef.Create("wg.lattice.haven_load_doubling", 600f, CVar.SERVERONLY);

    /// <summary>After Tacet, how much likelier and how much sooner again the leviathan comes.</summary>
    public static readonly CVarDef<float> TacetLeviathanScale =
        CVarDef.Create("wg.lattice.tacet_leviathan_scale", 3f, CVar.SERVERONLY);
}
