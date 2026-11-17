using Robust.Shared.Configuration;

namespace Content.Shared._WG.Remade.Dread;

/// <summary>Settings for the dread a Monolith puts on the crew.</summary>
[CVarDefs]
public sealed class WGDreadCVars
{
    /// <summary>Whether Rapture and the Crescendo reach players far from the stone, on any map.</summary>
    public static readonly CVarDef<bool> Sector =
        CVarDef.Create("wg.remade.dread.sector", true, CVar.SERVERONLY);

    /// <summary>Multiplies how often whispers, glimpses and pulses come. Zero silences them.</summary>
    public static readonly CVarDef<float> Rate =
        CVarDef.Create("wg.remade.dread.rate", 1f, CVar.SERVERONLY);

    /// <summary>Scales the glimpses and the warped sight on this client. Zero turns them off. Reduced motion turns them off too.</summary>
    public static readonly CVarDef<float> Visuals =
        CVarDef.Create("wg.remade.dread.visuals", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Scales the whisper bed under a Monolith's influence on this client. Zero turns it off. Muted ambience turns it off too.</summary>
    public static readonly CVarDef<float> Bed =
        CVarDef.Create("wg.remade.dread.bed", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
