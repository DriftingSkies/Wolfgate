using System.Linq;
using System.Numerics;
using Content.Server._WG.Lattice;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Director;
using Content.Server._WG.Remade.Torment;
using Content.Server._WG.Remade.Gifts;
using Content.Server._WG.Remade.Monolith;
using Content.Server.Chat.Managers;
using Content.Shared._WG.Lattice;
using Content.Shared._WG.Remade;
using Content.Shared._WG.Remade.Dread;
using Content.Shared.Chat;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.Remade.Dread;

/// <summary>What the stone has done to one player's head, and when it may do more.</summary>
[RegisterComponent]
public sealed partial class WGDreadSubjectComponent : Component
{
    [ViewVariables]
    public TimeSpan NextWhisper;

    [ViewVariables]
    public TimeSpan NextGlimpse;

    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public TimeSpan NextMurmur;

    /// <summary>The whisper bed last sent, from 0 to 1.</summary>
    [ViewVariables]
    public float Bed;

    /// <summary>The warp of sight last sent, and when it must be sent again to hold.</summary>
    [ViewVariables]
    public float Level;

    [ViewVariables]
    public TimeSpan LevelRenew;

    /// <summary>How many of each have been sent. Tests read these.</summary>
    [ViewVariables]
    public int Whispers;

    [ViewVariables]
    public int Glimpses;

    [ViewVariables]
    public int Pulses;

    [ViewVariables]
    public int Murmurs;
}

/// <summary>
/// The stone in the head. Every player under a Monolith's influence gets whispers, glimpses of the Remade at the edge of
/// sight and a warp of the screen, all more often with the stone's stage and the player's own stress. The crew near the stone
/// get the most. At Rapture, lightly, and at the Crescendo, more so, everyone in the sector gets them too, on any map. A shard of
/// a Scion does the same in a small way to whoever carries it or stands near it, weaker than the stone and by the Scion's stage.
/// From Harvest a low whisper bed also plays in the player's head, and at Rapture and the Crescendo it never leaves.
/// </summary>
public sealed partial class WGDreadSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCorruptionSystem _corruption = default!;
    [Dependency] private WGDriftMindSystem _drift = default!;
    [Dependency] private WGLatticeSystem _lattice = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);

    /// <summary>Drift a second the stone puts in a head at full dread.</summary>
    private const float DreadDrift = 1.5f;
    private static readonly TimeSpan WhisperGap = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MurmurGap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GlimpseGap = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PulseGap = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LevelHold = TimeSpan.FromSeconds(6);
    private static readonly Color WhisperColor = Color.FromHex("#9b2a2a");

    /// <summary>How many lines each stage adds to the whispers: the Hymn also has the older ones in the Director's list.</summary>
    private const int WhispersPerStage = 12;
    private const int LegacyWhispers = 8;

    private const string VoiceSounds = "WGDreadVoices";
    private const string MenaceSounds = "WGDreadMenace";
    private const string WhisperSounds = "WGDreadWhispers";

    private readonly record struct Source(EntityUid Uid, WGMonolithStage Stage, MapCoordinates At, bool Shard = false,
        bool Rooted = false, EntityUid? Carrier = null, EntityUid? LocalGrid = null, bool Shielded = false);

    private readonly List<Source> _sources = new();
    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TickRate;
        Sweep(now);
    }

    private void Sweep(TimeSpan now)
    {
        _sources.Clear();
        var monoliths = EntityQueryEnumerator<WGMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (monolith.Stage != WGMonolithStage.Quiet && xform.MapID != MapId.Nullspace)
            {
                var at = _transform.GetMapCoordinates(uid, xform);
                _sources.Add(new Source(uid, monolith.Stage, at, LocalGrid: monolith.Local ? xform.GridUid ?? uid : null, Shielded: _lattice.IsShielded(at)));
            }
        }

        // A shard stands in for its Scion at a short reach, and goes quiet near a main station like everything else.
        var shards = EntityQueryEnumerator<WGMonolithShardComponent, TransformComponent>();
        while (shards.MoveNext(out var uid, out var shard, out var xform))
        {
            if (shard.Parent is not { } parent || TerminatingOrDeleted(parent) || !TryComp<WGMonolithComponent>(parent, out var parentStone)
                || parentStone.Stage == WGMonolithStage.Quiet || xform.MapID == MapId.Nullspace || _corruption.IsSanctuaryAt(uid))
            {
                continue;
            }

            var shardAt = _transform.GetMapCoordinates(uid, xform);
            _sources.Add(new Source(uid, parentStone.Stage, shardAt, true, shard.Deployed, CarrierOf(uid), Shielded: _lattice.IsShielded(shardAt)));
        }

        var sectorWide = _config.GetCVar(WGDreadCVars.Sector);
        var rate = _config.GetCVar(WGDreadCVars.Rate);
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out _, out var xform))
        {
            if (HasComp<WGRemadeComponent>(uid) || _mobState.IsDead(uid) || xform.MapID == MapId.Nullspace)
                continue;

            var here = _transform.GetMapCoordinates(uid, xform);
            var stress = CompOrNull<WGRemadeStressComponent>(uid)?.Stress ?? 0f;
            var influence = 0f;
            var heard = 0f;
            var stage = WGMonolithStage.Quiet;
            var distance = 0f;
            var sameMap = false;
            var shardInfluence = 0f;
            var shardStage = WGMonolithStage.Quiet;

            // A sealed hex field keeps out everything of a Monolith that is outside it.
            var shielded = _sources.Count > 0 && _lattice.IsShielded(here);
            foreach (var source in _sources)
            {
                if (shielded && !source.Shielded)
                    continue;

                var same = source.At.MapId == here.MapId;
                var away = same ? (source.At.Position - here.Position).Length() : 0f;
                if (source.Shard)
                {
                    var near = same ? WGDreadMath.ShardInfluence(source.Stage, away, source.Carrier == uid, source.Rooted) : 0f;
                    if (near > shardInfluence || near == shardInfluence && near > 0f && source.Stage > shardStage)
                    {
                        shardInfluence = near;
                        shardStage = source.Stage;
                    }

                    continue;
                }

                if (source.LocalGrid is { } localGrid && xform.GridUid != localGrid)
                    continue;

                var share = WGDreadMath.Influence(source.Stage, away, same, sectorWide && source.LocalGrid == null);
                heard = Math.Max(heard, WGDreadMath.Influence(source.Stage, away, same, false));
                if (share > influence || share == influence && source.Stage > stage)
                {
                    influence = share;
                    stage = source.Stage;
                    distance = away;
                    sameMap = same;
                }
            }

            // An admin's torment: the whole Crescendo, wherever they are.
            if (HasComp<WGTormentedComponent>(uid))
            {
                influence = 1f;
                heard = 1f;
                stage = WGMonolithStage.Crescendo;
                distance = 0f;
                sameMap = true;
                shardInfluence = 0f;
                stress = Math.Max(stress, WGTormentedComponent.Stress);
            }

            var subject = EnsureComp<WGDreadSubjectComponent>(uid);
            if (influence <= 0f && shardInfluence <= 0f)
            {
                if (subject.Level > 0f || subject.Bed > 0f)
                    SendLevel(actor.PlayerSession, subject, 0f, 0f, 0f, now);

                continue;
            }

            // Whichever reaches the player harder sets what they get; the warp takes the greater of the two.
            var fromShard = shardInfluence > influence;
            var level = Math.Max(WGDreadMath.Level(stage, distance, sameMap, stress, sectorWide),
                WGDreadMath.ShardLevel(shardStage, shardInfluence, stress));
            if (fromShard)
            {
                influence = shardInfluence;
                stage = shardStage;
            }

            if (fromShard)
                heard = influence;

            // The stone's hold on a head is Drift, so it shows on the gauge and builds the Unmooring's hallucinations too.
            _drift.Add(uid, level * DreadDrift * (float) TickRate.TotalSeconds, WGDriftFlavour.Monolith, Math.Min(1f, level * 1.5f) * 100f);

            Roll(uid, subject, stage, influence * WGDreadMath.StressFactor(stress) * rate, heard * WGDreadMath.StressFactor(stress) * rate, now);

            var bed = WGDreadMath.Bed(stage, influence, stress, !fromShard);
            if (MathF.Abs(level - subject.Level) > 0.03f || MathF.Abs(bed - subject.Bed) > 0.03f
                || (level > 0f || bed > 0f) && now >= subject.LevelRenew)
            {
                SendLevel(actor.PlayerSession, subject, level, 0f, bed, now);
            }
        }
    }

    /// <summary>
    /// Gives a player a chance of each thing the stone can do in a second. Whispers and murmurs take <paramref name="heard"/>, the
    /// reach without the sector share, so far from the stone the dread is the score and the bed rather than voices.
    /// </summary>
    private void Roll(EntityUid uid, WGDreadSubjectComponent subject, WGMonolithStage stage, float factor, float heard, TimeSpan now)
    {
        if (now >= subject.NextWhisper && _random.Prob(WGDreadMath.Chance(WGDreadMath.WhispersPerMinute(stage) * heard, 1f)))
        {
            subject.NextWhisper = now + WhisperGap;
            Whisper(uid, stage);
        }

        if (now >= subject.NextMurmur && _random.Prob(WGDreadMath.Chance(WGDreadMath.MurmursPerMinute(stage) * heard, 1f)))
        {
            subject.NextMurmur = now + MurmurGap;
            Murmur(uid, stage);
        }

        if (now >= subject.NextGlimpse && _random.Prob(WGDreadMath.Chance(WGDreadMath.GlimpsesPerMinute(stage) * factor, 1f)))
        {
            subject.NextGlimpse = now + GlimpseGap;
            Glimpse(uid, stage);
        }

        if (now >= subject.NextPulse && _random.Prob(WGDreadMath.Chance(WGDreadMath.PulsesPerMinute(stage) * factor, 1f)))
        {
            subject.NextPulse = now + PulseGap;
            Pulse(uid, 0.4f + 0.12f * (int) stage);
        }
    }

    private void SendLevel(ICommonSession session, WGDreadSubjectComponent subject, float level, float spike, float bed, TimeSpan now)
    {
        subject.Level = level;
        subject.Bed = bed;
        subject.LevelRenew = now + LevelHold / 2;
        RaiseNetworkEvent(new WGDreadVisionEvent(level, spike, (float) LevelHold.TotalSeconds, bed), session);
    }

    /// <summary>The mob carrying a thing, however deep in bags or hands, if it is a player.</summary>
    private EntityUid? CarrierOf(EntityUid thing)
    {
        var current = thing;
        for (var depth = 0; depth < 6 && _container.TryGetContainingContainer((current, null, null), out var container); depth++)
        {
            current = container.Owner;
            if (HasComp<ActorComponent>(current))
                return current;
        }

        return null;
    }

    /// <summary>A throb of the vignette and a drain of colour on top of whatever warp the player has.</summary>
    public void Pulse(EntityUid target, float spike)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        var subject = EnsureComp<WGDreadSubjectComponent>(target);
        subject.Pulses++;
        RaiseNetworkEvent(new WGDreadVisionEvent(subject.Level, Math.Clamp(spike, 0f, 1f), (float) LevelHold.TotalSeconds, subject.Bed), actor.PlayerSession);
    }

    /// <summary>Shows a player a Remade at the edge of their sight for a moment. The client finds the dark spot.</summary>
    public void Glimpse(EntityUid target, WGMonolithStage stage)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        var options = WGDreadGlimpses.Available(stage).ToList();
        if (options.Count == 0)
            return;

        EnsureComp<WGDreadSubjectComponent>(target).Glimpses++;
        RaiseNetworkEvent(new WGDreadGlimpseEvent(_random.Pick(options), _random.NextFloat(0.4f, 0.8f)), actor.PlayerSession);
    }

    /// <summary>A line in the player's head, with a voice behind them about half the time. Louder things join it with the stage.</summary>
    public void Whisper(EntityUid target, WGMonolithStage stage)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        EnsureComp<WGDreadSubjectComponent>(target).Whispers++;
        var text = Loc.GetString(WhisperKey(stage), ("name", Name(target)));
        _popup.PopupEntity(text, target, target, stage >= WGMonolithStage.Rapture ? PopupType.LargeCaution : PopupType.MediumCaution);
        _chat.ChatMessageToOne(ChatChannel.Server, text, $"[italic]{FormattedMessage.EscapeText(text)}[/italic]", default, false,
            actor.PlayerSession.Channel, WhisperColor);

        if (_random.Prob(0.85f))
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(2.5f, 7f);
            _audio.PlayStatic(WhisperSound(stage), actor.PlayerSession, Transform(target).Coordinates.Offset(offset), AudioParams.Default.WithVolume(-3f));
        }
    }

    /// <summary>Whispering with no words to read: close, quiet and layered more thickly as the stage climbs. Several can overlap.</summary>
    public void Murmur(EntityUid target, WGMonolithStage stage)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        EnsureComp<WGDreadSubjectComponent>(target).Murmurs++;
        var layers = stage >= WGMonolithStage.Crescendo ? 3 : stage >= WGMonolithStage.Rapture ? 2 : 1;
        for (var layer = 0; layer < layers; layer++)
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(0.8f, 4.5f);
            _audio.PlayStatic(MurmurSound(), actor.PlayerSession, Transform(target).Coordinates.Offset(offset),
                AudioParams.Default.WithVolume(_random.NextFloat(-9f, -5f)).WithVariation(0.06f));
        }
    }

    /// <summary>A whisper from the pool of cuts, for a place that wants one without the stage's rarer sounds.</summary>
    public SoundSpecifier MurmurSound() => new SoundCollectionSpecifier(WhisperSounds);

    /// <summary>The Fluent id of a whisper fit for a stage: mostly its own tier, now and then an older, quieter one.</summary>
    public string WhisperKey(WGMonolithStage stage)
    {
        var tier = stage;
        if (tier == WGMonolithStage.Quiet)
            tier = WGMonolithStage.Hymn;

        if (tier > WGMonolithStage.Hymn && !_random.Prob(0.6f))
            tier = (WGMonolithStage) _random.Next((int) WGMonolithStage.Hymn, (int) tier);

        return WhisperKeyAt(tier, _random.Next(tier == WGMonolithStage.Hymn ? WhispersPerStage + LegacyWhispers : WhispersPerStage));
    }

    /// <summary>The id of the n-th whisper of a tier. At the Hymn the first lines are the Director's older ones.</summary>
    public static string WhisperKeyAt(WGMonolithStage tier, int n)
    {
        if (tier == WGMonolithStage.Hymn && n < LegacyWhispers)
            return $"wg-monolith-whisper-{n + 1}";

        if (tier == WGMonolithStage.Hymn)
            n -= LegacyWhispers;

        return $"wg-dread-whisper-{tier.ToString().ToLowerInvariant()}-{n + 1}";
    }

    /// <summary>Every whisper id there is, for a check that each has its words.</summary>
    public static IEnumerable<string> AllWhisperKeys()
    {
        for (var n = 0; n < WhispersPerStage + LegacyWhispers; n++)
        {
            yield return WhisperKeyAt(WGMonolithStage.Hymn, n);
        }

        for (var tier = WGMonolithStage.Growth; tier <= WGMonolithStage.Crescendo; tier++)
        {
            for (var n = 0; n < WhispersPerStage; n++)
            {
                yield return WhisperKeyAt(tier, n);
            }
        }
    }

    /// <summary>
    /// A sound for a whisper: nearly always whispering, with the spoken voices and the Remade's own noises mixed in more thinly
    /// as the stage climbs.
    /// </summary>
    private SoundSpecifier WhisperSound(WGMonolithStage stage)
    {
        var (voices, menace) = stage switch
        {
            WGMonolithStage.Growth => (0.12f, 0f),
            WGMonolithStage.Harvest => (0.15f, 0.15f),
            WGMonolithStage.Rapture => (0.15f, 0.25f),
            WGMonolithStage.Crescendo => (0.15f, 0.3f),
            _ => (0f, 0f),
        };

        var roll = _random.NextFloat();
        if (roll < menace)
            return new SoundCollectionSpecifier(MenaceSounds);

        return roll < menace + voices ? new SoundCollectionSpecifier(VoiceSounds) : MurmurSound();
    }
}
