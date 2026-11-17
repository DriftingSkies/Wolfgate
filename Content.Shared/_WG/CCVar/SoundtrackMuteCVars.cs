using Robust.Shared.Configuration;

namespace Content.Shared._WG.CCVar;

/// <summary>Settings for muting background music near a jukebox or instrument.</summary>
[CVarDefs]
public sealed class SoundtrackMuteCVars
{
    /// <summary>Whether background music fades out while a jukebox or instrument is in range.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.soundtrack_mute.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
