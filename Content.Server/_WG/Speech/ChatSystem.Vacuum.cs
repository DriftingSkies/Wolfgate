using Content.Server.Atmos.EntitySystems;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._WG.CCVar;
using Content.Shared.Atmos;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    [Dependency] private AtmosphereSystem _wgAtmosphere = default!;

    /// <summary>
    /// Whether the air around a speaker is too thin to carry a spoken <paramref name="language"/>, so it only reaches
    /// whisper range. Languages that need no sound, such as sign, are never muffled.
    /// </summary>
    public bool WgSpeechMuffledByVacuum(EntityUid source, LanguagePrototype language)
    {
        if (!language.SpeechOverride.RequireSpeech || !_configurationManager.GetCVar(SpeechCVars.VacuumWhisper))
            return false;
        return _wgAtmosphere.GetContainingMixture(source) is not { } air || air.Pressure <= Atmospherics.HazardLowPressure;
    }
}
