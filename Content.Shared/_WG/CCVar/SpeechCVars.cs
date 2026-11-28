using Robust.Shared.Configuration;

namespace Content.Shared._WG.CCVar;

/// <summary>Settings for how speech carries.</summary>
[CVarDefs]
public sealed class SpeechCVars
{
    /// <summary>Whether speech in air at or below hazard low pressure only carries as a whisper.</summary>
    public static readonly CVarDef<bool> VacuumWhisper =
        CVarDef.Create("wg.speech.vacuum_whisper", true, CVar.SERVERONLY);
}
