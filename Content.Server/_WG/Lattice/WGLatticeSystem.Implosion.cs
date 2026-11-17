using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared._WG.Lattice;
using Content.Shared.Camera;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WG.Lattice;

/// <summary>
/// Kyphrus's implosion at Tacet (<see cref="WGLatticeImplosion"/>): the sounds of its swell, fall and burst, then the
/// shockwaves that run out across the sector, bursting bulbs and throwing ships and anyone in open space about. Mobile
/// relays are the last havens after the Unstringing, and their power draw doubles on and on.
/// </summary>
public sealed partial class WGLatticeSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private static readonly SoundSpecifier CollapseMusic = new SoundPathSpecifier("/Audio/_WG/Lattice/tacet_spleen.ogg",
        AudioParams.Default.WithVolume(2f));
    private static readonly SoundSpecifier SwellRumble = new SoundPathSpecifier("/Audio/_WG/LivingWorld/ambience/o_ambience_disaster_rumble1.ogg",
        AudioParams.Default.WithVolume(4f));
    private static readonly SoundSpecifier SwellForm = new SoundPathSpecifier("/Audio/Effects/singularity_form.ogg",
        AudioParams.Default.WithPitchScale(0.5f));
    private static readonly SoundSpecifier CollapseSound = new SoundPathSpecifier("/Audio/Effects/singularity_collapse.ogg",
        AudioParams.Default.WithPitchScale(0.7f).WithVolume(4f));
    private static readonly SoundSpecifier ChargeSound = new SoundPathSpecifier("/Audio/Effects/Grenades/SelfDestruct/SDS_Charge.ogg",
        AudioParams.Default.WithPitchScale(0.6f).WithVolume(2f));
    private static readonly SoundSpecifier RiseSound = new SoundPathSpecifier("/Audio/Effects/Grenades/Supermatter/whitehole_start.ogg",
        AudioParams.Default.WithVolume(4f));
    private static readonly SoundSpecifier BurstSound = new SoundPathSpecifier("/Audio/_WG/Lattice/tacet_burst.ogg",
        AudioParams.Default.WithVolume(6f));
    private static readonly SoundSpecifier WaveSound = new SoundPathSpecifier("/Audio/Effects/explosionfar.ogg",
        AudioParams.Default.WithVolume(4f));

    /// <summary>Sounds through the implosion: seconds in, and what plays to everyone.</summary>
    private static readonly (float At, SoundSpecifier Sound)[] ImplosionSounds =
    {
        (0f, CollapseMusic),
        (0f, SwellRumble),
        (0.5f, SwellForm),
        (WGLatticeImplosion.Swell, CollapseSound),
        (WGLatticeImplosion.Collapse + 2f, ChargeSound),
        (WGLatticeImplosion.Burst - 3.2f, RiseSound),
        (WGLatticeImplosion.Burst, BurstSound),
    };

    /// <summary>Least and most a shockwave runs out from the centre, in metres, and how far past the farthest ship.</summary>
    private const float MinWaveReach = 70000f;
    private const float MaxWaveReach = 200000f;
    private const float WaveOvershoot = 5000f;

    private static readonly TimeSpan WaveKnockdown = TimeSpan.FromSeconds(2);

    /// <summary>An implosion under way on a map: the next sound to play and how far each shockwave has got.</summary>
    private sealed class Implosion
    {
        public int Sound;
        public bool Burst;
        public readonly float[] Fronts = new float[WGLatticeImplosion.Waves];
    }

    private readonly Dictionary<EntityUid, Implosion> _implosions = new();

    /// <summary>When each map was unstrung, for its havens' climbing power draw.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _unstrungAt = new();

    private readonly HashSet<EntityUid> _waveGrids = new();
    private readonly List<(EntityUid Uid, Vector2 Position)> _waveLoose = new();

    private void StartImplosion(EntityUid map)
    {
        _implosions[map] = new Implosion();
    }

    /// <summary>A player-built relay, which holds as a haven after the Unstringing; a station's mapped anchor isn't one.</summary>
    private bool IsMobileRelay(EntityUid uid)
    {
        return TryComp<WGLatticeRelayComponent>(uid, out var relay) && relay.NeedsFuel;
    }

    /// <summary>How many times its usual draw a haven on this map pulls: doubling every so often after the Unstringing.</summary>
    private float HavenLoadFactor(EntityUid? map)
    {
        if (map is not { } m || !_unstrungAt.TryGetValue(m, out var since))
            return 1f;

        var doubling = MathF.Max(1f, _config.GetCVar(WGLatticeEndgameCVars.HavenLoadDoubling));
        var doublings = (float) (_timing.CurTime - since).TotalSeconds / doubling;
        return MathF.Pow(2f, Math.Clamp(doublings, 0f, 24f));
    }

    /// <summary>Runs each implosion: its sounds, its burst and its shockwaves. Every frame, so the waves hit on time.</summary>
    private void UpdateImplosions()
    {
        if (_implosions.Count == 0)
            return;

        var now = _timing.CurTime;
        foreach (var (map, implosion) in _implosions)
        {
            if (!TryComp<WGLatticeFieldComponent>(map, out var field))
            {
                _implosions.Remove(map);
                return;
            }

            var seconds = (float) (now - field.TacetStart).TotalSeconds;
            while (implosion.Sound < ImplosionSounds.Length && seconds >= ImplosionSounds[implosion.Sound].At)
            {
                _audio.PlayGlobal(ImplosionSounds[implosion.Sound].Sound, Filter.Broadcast(), true);
                implosion.Sound++;
            }

            if (!implosion.Burst && seconds >= WGLatticeImplosion.Burst)
            {
                implosion.Burst = true;
                field.WaveReach = WaveReach(map);
                Dirty(map, field);
                Announce("wg-lattice-tacet", "wg-lattice-tacet-title", "wg-lattice-tacet-chip", TacetSound);
                ShakeEveryone(map);
            }

            if (!implosion.Burst)
                continue;

            RunWaves(map, field, implosion, seconds);
            if (WGLatticeImplosion.WaveRadius(seconds, WGLatticeImplosion.Waves - 1) <= field.WaveReach)
                continue;

            // The last wave has gone by: from here the bulbs just keep failing.
            _tacetAt[map] = now + TimeSpan.FromSeconds(_config.GetCVar(WGLatticeEndgameCVars.DarkInterval));
            _implosions.Remove(map);
            return;
        }
    }

    /// <summary>How far out the waves need to run: past the farthest ship, within limits.</summary>
    private float WaveReach(EntityUid map)
    {
        var farthest = 0f;
        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == map)
                farthest = MathF.Max(farthest, _transform.GetWorldPosition(xform).Length());
        }

        return Math.Clamp(farthest + WaveOvershoot, MinWaveReach, MaxWaveReach);
    }

    /// <summary>The burst itself: every camera on the map jolts.</summary>
    private void ShakeEveryone(EntityUid map)
    {
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == map)
                _recoil.KickCamera(uid, _random.NextAngle().ToVec() * 4f);
        }
    }

    /// <summary>Moves each shockwave on and hits whatever its front passed over since the last frame.</summary>
    private void RunWaves(EntityUid map, WGLatticeFieldComponent field, Implosion implosion, float seconds)
    {
        _waveGrids.Clear();
        _waveLoose.Clear();
        for (var i = 0; i < WGLatticeImplosion.Waves; i++)
        {
            var front = MathF.Min(WGLatticeImplosion.WaveRadius(seconds, i), field.WaveReach);
            var from = implosion.Fronts[i];
            if (front <= from)
                continue;

            implosion.Fronts[i] = front;
            var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (grids.MoveNext(out var grid, out _, out var xform))
            {
                if (xform.MapUid != map)
                    continue;

                var distance = _transform.GetWorldPosition(xform).Length();
                if (distance > from && distance <= front)
                    _waveGrids.Add(grid);
            }

            // People adrift in open space are thrown too.
            var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
            while (actors.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid != map || xform.GridUid != null)
                    continue;

                var position = _transform.GetWorldPosition(xform);
                var distance = position.Length();
                if (distance > from && distance <= front)
                    _waveLoose.Add((uid, position));
            }
        }

        if (_waveGrids.Count > 0)
            HitGrids();

        foreach (var (uid, position) in _waveLoose)
        {
            Shove(uid, position);
            _recoil.KickCamera(uid, Outward(position) * 3f);
        }
    }

    /// <summary>The wave goes through every ship it reached this frame: bulbs burst, the ship is thrown, its crew knocked flat.</summary>
    private void HitGrids()
    {
        var chance = _config.GetCVar(WGLatticeEndgameCVars.WaveBulbChance);
        var lights = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (lights.MoveNext(out var uid, out var light, out var xform))
        {
            if (xform.GridUid is { } grid && _waveGrids.Contains(grid) && _random.Prob(chance))
                _lights.TryDestroyBulb(uid, light);
        }

        foreach (var grid in _waveGrids)
        {
            Shove(grid, _transform.GetWorldPosition(grid));
            _audio.PlayPvs(WaveSound, grid);
        }

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || !_waveGrids.Contains(grid))
                continue;

            _recoil.KickCamera(uid, Outward(_transform.GetWorldPosition(xform)) * 4f);
            _stun.TryKnockdown(uid, WaveKnockdown, true);
            _popup.PopupEntity(Loc.GetString("wg-lattice-tacet-wave"), uid, uid, PopupType.LargeCaution);
        }
    }

    /// <summary>Throws something outward from the centre and sets it spinning, whatever its mass.</summary>
    private void Shove(EntityUid uid, Vector2 position)
    {
        if (!TryComp<PhysicsComponent>(uid, out var body) || body.BodyType != BodyType.Dynamic)
            return;

        var outward = Outward(position);
        var across = new Vector2(-outward.Y, outward.X) * _random.NextFloat(-0.35f, 0.35f);
        var shove = _config.GetCVar(WGLatticeEndgameCVars.WaveShove);
        _physics.ApplyLinearImpulse(uid, (outward + across) * shove * body.Mass, body: body);

        var spin = _config.GetCVar(WGLatticeEndgameCVars.WaveSpin) * _random.NextFloat(0.5f, 1f) * (_random.Prob(0.5f) ? 1f : -1f);
        _physics.ApplyAngularImpulse(uid, spin * body.Inertia, body: body);
    }

    private Vector2 Outward(Vector2 position)
    {
        return position.LengthSquared() > 1f ? Vector2.Normalize(position) : _random.NextAngle().ToVec();
    }
}
