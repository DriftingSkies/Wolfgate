using Content.Shared._WF.Lattice;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._WF.Lattice;

/// <summary>
/// Near a reality anchor the local player hears it working: a low alien hum that swells as they close on it and fades
/// out a few hundred metres away. Only this client hears it, and only one hum plays, for the nearest anchor.
/// </summary>
public sealed partial class WFLatticeHumSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly SoundSpecifier Hum = new SoundPathSpecifier("/Audio/_Goobstation/Flock/Flock_Reactor.ogg");

    /// <summary>Furthest the hum carries, in metres.</summary>
    private const float Range = 450f;

    /// <summary>The hum's loudness at the edge of its range and beside the anchor, in decibels.</summary>
    private const float Faint = -28f;
    private const float Near = -4f;

    private EntityUid? _playing;

    public override void Shutdown()
    {
        base.Shutdown();
        Stop();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var distance = NearestAnchor();
        if (distance is not { } metres || metres > Range)
        {
            Stop();
            return;
        }

        var closeness = 1f - metres / Range;
        var volume = Faint + (Near - Faint) * closeness * closeness;
        if (_playing is not { } hum || !Exists(hum))
        {
            _playing = _audio.PlayGlobal(Hum, Filter.Local(), false,
                AudioParams.Default.WithLoop(true).WithVolume(volume))?.Entity;
            return;
        }

        _audio.SetVolume(hum, volume);
    }

    /// <summary>Metres from the local player to the nearest anchor on their map, if there is a Lattice there.</summary>
    private float? NearestAnchor()
    {
        if (_player.LocalEntity is not { } mob
            || Transform(mob).MapUid is not { } map
            || !TryComp<WFLatticeFieldComponent>(map, out var field)
            || field.Anchors.Count == 0)
            return null;

        var position = _transform.GetWorldPosition(mob);
        var best = float.MaxValue;
        foreach (var anchor in field.Anchors)
        {
            best = MathF.Min(best, (anchor - position).Length());
        }

        return best;
    }

    private void Stop()
    {
        if (_playing is { } hum)
            _audio.Stop(hum);

        _playing = null;
    }
}
