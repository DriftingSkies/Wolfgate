using System.Numerics;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Physics.Systems;

namespace Content.Client._WF.Audio;

/// <summary>
/// Takes Doppler off looping sounds. OpenAL Soft 1.24.3 writes past its mix buffer when a pitched-up loop wraps,
/// which crashes the client; a fast missile's engine loop does it.
/// </summary>
public sealed class WFLoopDopplerSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        // The audio system gives every source its entity's velocity; this has to overwrite that.
        UpdatesAfter.Add(typeof(AudioSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        // A source moving with the listener has no relative speed, so OpenAL leaves its pitch alone.
        var listener = _player.LocalEntity is { } local ? _physics.GetMapLinearVelocity(local) : Vector2.Zero;

        var query = AllEntityQuery<AudioComponent>();
        while (query.MoveNext(out var audio))
        {
            var audioParams = audio.Params;
            if (audioParams.Loop)
                audio.Velocity = listener;
        }
    }
}
