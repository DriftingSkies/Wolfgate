using Robust.Shared.Configuration;

namespace Content.Shared._WG.MarkingGlow;

/// <summary>Server settings for markings, eyes and hair players choose to make glow.</summary>
[CVarDefs]
public sealed class MarkingGlowCVars
{
    /// <summary>
    /// Whether characters' chosen markings, eyes and hair glow. Off, they are drawn lit and the creator hides its
    /// switches; the choices stay saved. Replicated so clients draw and offer them alike.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.marking_glow.enabled", true, CVar.SERVER | CVar.REPLICATED);
}
