using Robust.Shared.Configuration;

namespace Content.Shared._WG.CombatFacing;

/// <summary>Combat mode facing settings.</summary>
[CVarDefs]
public sealed class WGCombatFacingCVars
{
    /// <summary>
    /// Turns you toward the cursor while in combat mode. Off, you turn by moving as usual. Replicated so the server
    /// knows each player's choice.
    /// </summary>
    public static readonly CVarDef<bool> FaceCursor =
        CVarDef.Create("wg.combat_face_cursor", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);
}
