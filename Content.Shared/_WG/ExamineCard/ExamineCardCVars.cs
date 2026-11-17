using Robust.Shared.Configuration;

namespace Content.Shared._WG.ExamineCard;

/// <summary>Server settings for the character examine card.</summary>
[CVarDefs]
public sealed class ExamineCardCVars
{
    /// <summary>Whether examining a character shows the ID card instead of the old worn-item lines.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.examine_card.enabled", true, CVar.SERVERONLY);

    /// <summary>Whether a character is told in chat when someone examines them.</summary>
    public static readonly CVarDef<bool> Notice =
        CVarDef.Create("wg.examine_card.notice", true, CVar.SERVERONLY);
}
