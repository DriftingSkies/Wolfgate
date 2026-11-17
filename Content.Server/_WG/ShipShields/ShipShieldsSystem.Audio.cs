using System.Numerics;
using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private IGameTiming _wgShieldTiming = default!;
    [Dependency] private IRobustRandom _wgShieldRandom = default!;
    [Dependency] private EntityLookupSystem _wgShieldAudioLookup = default!;
    private static readonly SoundCollectionSpecifier WolfgateImpactSound = new("WGShipShieldImpacts");

    /// <summary>Plays a generator transition with a shared five-second hull cooldown.</summary>
    public void PlayWolfgateShieldPowerSound(EntityUid emitterUid, EntityUid grid, bool powered)
    {
        if (TerminatingOrDeleted(grid) || !TryComp<ShipShieldEmitterComponent>(emitterUid, out var emitter))
            return;
        var state = EnsureComp<WGShipShieldImpactAudioComponent>(grid);
        var now = _wgShieldTiming.CurTime;
        if (now < state.NextPowerSound)
            return;
        state.NextPowerSound = now + TimeSpan.FromSeconds(5);
        var sound = powered ? emitter.PowerUpSound : emitter.PowerDownSound;
        var bounds = _wgShieldAudioLookup.GetWorldAABB(grid);
        var center = _wgShieldMap.GetGridPosition(grid);
        var extent = Vector2.Max(Vector2.Abs(bounds.BottomLeft - center), Vector2.Abs(bounds.TopRight - center));
        var radius = MathF.Max(1f, extent.Length());
        if (_audio.PlayPvs(sound, grid, sound.Params.WithReferenceDistance(radius)
                .WithMaxDistance(radius + SharedAudioSystem.DefaultSoundRange)) is not { } stream)
            return;
        stream.Component.Flags |= AudioFlags.GridAudio | AudioFlags.NoOcclusion;
        _pvsSys.AddGlobalOverride(stream.Entity);
        Dirty(stream.Entity, stream.Component);
    }

    /// <summary>Plays frequent hull-wide impacts with at most two overlapping echoes.</summary>
    private void PlayWolfgateShieldImpact(EntityUid shield, Vector2 position)
    {
        if (!TryComp<ShipShieldComponent>(shield, out var shieldComponent) ||
            TerminatingOrDeleted(shieldComponent.Shielded))
            return;
        var grid = shieldComponent.Shielded;
        var state = EnsureComp<WGShipShieldImpactAudioComponent>(grid);
        var now = _wgShieldTiming.CurTime;
        if (now < state.NextImpactSound)
            return;
        state.NextImpactSound = now + TimeSpan.FromSeconds(_wgShieldRandom.NextFloat(0.75f, 1.1f));
        var bounds = _wgShieldAudioLookup.GetWorldAABB(grid);
        var center = _wgShieldMap.GetGridPosition(grid);
        var extent = Vector2.Max(Vector2.Abs(bounds.BottomLeft - center), Vector2.Abs(bounds.TopRight - center));
        var radius = MathF.Max(1f, extent.Length());
        var sound = _audio.PlayPvs(WolfgateImpactSound, grid,
            AudioParams.Default.WithVolume(4f).WithReferenceDistance(radius)
                .WithMaxDistance(radius + SharedAudioSystem.DefaultSoundRange).WithVariation(0.025f));
        if (sound is { } stream)
        {
            _audio.Stop(state.PreviousImpactSound);
            state.PreviousImpactSound = state.ActiveImpactSound;
            state.ActiveImpactSound = stream.Entity;
            stream.Component.Flags |= AudioFlags.GridAudio | AudioFlags.NoOcclusion;
            _pvsSys.AddGlobalOverride(stream.Entity);
            Dirty(stream.Entity, stream.Component);
        }
    }
}
