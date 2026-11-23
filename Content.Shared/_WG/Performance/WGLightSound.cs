using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Performance;

/// <summary>Server cvars for light sounds.</summary>
[CVarDefs]
public sealed class WGLightSoundCVars
{
    /// <summary>
    /// Whether footsteps and gunfire reach clients as a message instead of a networked sound entity. Off plays them
    /// the upstream way.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.audio.light_sounds", true, CVar.SERVERONLY);
}

/// <summary>A short sound for a client to play on an entity it knows, sent in place of a sound entity.</summary>
[Serializable, NetSerializable]
public sealed class WGLightSoundEvent : EntityEventArgs
{
    public NetEntity Source;
    public ResolvedSoundSpecifier Sound = default!;
    public AudioParams Params;
}

/// <summary>
/// Plays short, frequent sounds without a server entity for each. Upstream spawns, networks and deletes one entity
/// per sound, about 270 a second on a full server; here the server sends one message to the players in range and
/// each client plays the sound itself. For sounds nobody needs to stop, move or hear the rest of on arriving late.
/// </summary>
public abstract class SharedWGLightSoundSystem : EntitySystem
{
    /// <summary>Plays a sound on an entity for everyone in range but the user, who predicts it.</summary>
    public abstract void PlayPredicted(SoundSpecifier? sound, EntityUid source, EntityUid? user, AudioParams? audioParams = null);

    /// <summary>Plays a sound on an entity for everyone in range.</summary>
    public abstract void PlayPvs(SoundSpecifier? sound, EntityUid source, AudioParams? audioParams = null);
}
