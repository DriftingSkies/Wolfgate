using Robust.Shared.Configuration;

namespace Content.Shared._WG.Remade.Signals;

/// <summary>Settings for the Signals.</summary>
[CVarDefs]
public sealed class WGSignalCVars
{
    /// <summary>Whether ghosts can join a Monolith as a Signal.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.remade.signals.enabled", true, CVar.SERVERONLY);
}
