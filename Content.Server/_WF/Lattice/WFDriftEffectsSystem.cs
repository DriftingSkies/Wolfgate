using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server.Chat.Managers;
using Content.Shared._WF.Lattice;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// What Drift does to the people out in it. The server picks each hallucination and sends it to the one player who has
/// it; nobody else sees or hears a thing. In the Lattice's last bands the Unmooring takes a ship's small animals, which
/// turn into Remade, unless the ship is sheltered.
/// </summary>
public sealed partial class WFDriftEffectsSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFRemadeConversionSystem _conversion = default!;

    private const int WhisperLines = 12;
    private const int RadioLines = 8;
    private const int HailLines = 6;
    private const int FakeShips = 8;
    private const int MimicLines = 12;

    /// <summary>How far away someone can be and still be seen as Remade or overheard saying what they didn't.</summary>
    private const float NearRange = 10f;
    private const string RadioColor = "#2cdb2c";

    private static readonly SoundSpecifier[] Sounds =
    {
        new SoundCollectionSpecifier("WFBetrayalSteps"),
        new SoundCollectionSpecifier("WFBetrayalKnocks"),
        new SoundCollectionSpecifier("WFBetrayalBreaths"),
        new SoundCollectionSpecifier("WFBetrayalScrapes"),
    };

    private static readonly SoundSpecifier Menace = new SoundCollectionSpecifier("WFDreadMenace");

    private static readonly SoundSpecifier PulseSound = new SoundPathSpecifier("/Audio/_WF/Lattice/drift_pulse.ogg");

    /// <summary>How long a pulse holds, in seconds.</summary>
    private const float PulseSeconds = 3.2f;

    private readonly Dictionary<NetUserId, TimeSpan> _next = new();
    private readonly List<WFLatticeEffect> _open = new();
    private readonly List<EntityUid> _animals = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ => _next.Clear());
    }

    /// <summary>Rolls this player's next hallucination, if one is due.</summary>
    public void Roll(ICommonSession session, EntityUid mob, float drift, WFLatticeProfilePrototype profile)
    {
        var now = _timing.CurTime;
        if (_next.TryGetValue(session.UserId, out var next) && now < next)
            return;

        var share = Math.Clamp(drift / profile.DriftMax, 0f, 1f);
        // The rolls come closer together as Drift climbs, until at full Drift something is always happening.
        var interval = MathHelper.Lerp(profile.EffectInterval, profile.EffectIntervalAtMax, MathF.Pow(share, 1.5f));
        _next[session.UserId] = now + TimeSpan.FromSeconds(interval * _random.NextFloat(0.6f, 1.4f));
        if (!_mobState.IsAlive(mob))
            return;

        _open.Clear();
        var total = 0f;
        foreach (var effect in profile.Effects)
        {
            if (drift < effect.MinDrift || effect.Weight <= 0f)
                continue;

            _open.Add(effect);
            total += effect.Weight;
        }

        if (_open.Count == 0 || !_random.Prob(MathHelper.Lerp(profile.EffectChance.X, profile.EffectChance.Y, share)))
            return;

        // Near the top they pile on each other.
        var count = share >= 0.95f ? 3 : share >= 0.8f ? 2 : 1;
        for (var i = 0; i < count; i++)
        {
            Pick(session, mob, total);
        }
    }

    private void Pick(ICommonSession session, EntityUid mob, float total)
    {
        var pick = _random.NextFloat(total);
        foreach (var effect in _open)
        {
            pick -= effect.Weight;
            if (pick > 0f)
                continue;

            Show(session, mob, effect.Kind);
            return;
        }
    }

    /// <summary>Shows one hallucination to one player.</summary>
    public void Show(ICommonSession session, EntityUid mob, WFLatticeHallucinationKind kind)
    {
        var seed = _random.Next();
        switch (kind)
        {
            case WFLatticeHallucinationKind.Whisper:
                _popup.PopupEntity(Loc.GetString($"wf-lattice-whisper-{_random.Next(1, WhisperLines + 1)}"), mob, session, PopupType.MediumCaution);
                return;
            case WFLatticeHallucinationKind.Radio:
                Radio(session, Loc.GetString("wf-lattice-radio-name"), Loc.GetString($"wf-lattice-radio-{_random.Next(1, RadioLines + 1)}"));
                return;
            case WFLatticeHallucinationKind.Hail:
                var ship = Loc.GetString($"wf-lattice-fake-ship-{_random.Next(1, FakeShips + 1)}");
                Radio(session, ship, Loc.GetString($"wf-lattice-hail-{_random.Next(1, HailLines + 1)}", ("ship", ship)));
                return;
            case WFLatticeHallucinationKind.Sound:
                _audio.PlayStatic(_random.Pick(Sounds), Filter.SinglePlayer(session), Near(mob, 3f, 6f), true);
                return;
            case WFLatticeHallucinationKind.Blip:
                // A contact somewhere out in the dark round the ship; now and then a true one.
                var contact = _random.Prob(TrueBlipChance) && TrueThing(mob, BlipRange) is { } real
                    ? real
                    : Near(mob, 400f, 1500f, true);
                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(contact), 25f, seed), session);
                return;
            case WFLatticeHallucinationKind.Glimpse:
                Shared(session, mob, new WFLatticeHallucinationEvent(kind, GetNetCoordinates(Near(mob, 4f, 7f)), 0.6f, seed));
                return;
            case WFLatticeHallucinationKind.Phantom:
                var at = Near(mob, 5f, 8f);
                Shared(session, mob, new WFLatticeHallucinationEvent(kind, GetNetCoordinates(at), 8f, seed));
                _audio.PlayStatic(Menace, Filter.SinglePlayer(session), at, true);
                return;
            case WFLatticeHallucinationKind.Pulse:
                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(Transform(mob).Coordinates), PulseSeconds, seed), session);
                _audio.PlayGlobal(PulseSound, session);
                return;
            case WFLatticeHallucinationKind.Bleed:
                Shared(session, mob, new WFLatticeHallucinationEvent(kind, GetNetCoordinates(Transform(mob).Coordinates), _random.NextFloat(6f, 14f), seed));
                return;
            case WFLatticeHallucinationKind.Mirror:
                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(new EntityCoordinates(mob, Vector2.Zero)), _random.NextFloat(2f, 6f), seed), session);
                _popup.PopupEntity(Loc.GetString("wf-lattice-mirror"), mob, session, PopupType.LargeCaution);
                return;
            case WFLatticeHallucinationKind.Swarm:
                Shared(session, mob, new WFLatticeHallucinationEvent(kind, GetNetCoordinates(Transform(mob).Coordinates), _random.NextFloat(1.2f, 2.5f), seed));
                _audio.PlayStatic(Menace, Filter.SinglePlayer(session), Transform(mob).Coordinates, true);
                return;
            case WFLatticeHallucinationKind.Turn:
                if (Someone(mob) is not { } other)
                {
                    Show(session, mob, WFLatticeHallucinationKind.Mirror);
                    return;
                }

                RaiseNetworkEvent(new WFLatticeHallucinationEvent(kind, GetNetCoordinates(new EntityCoordinates(other, Vector2.Zero)), _random.NextFloat(1.5f, 5f), seed), session);
                return;
            case WFLatticeHallucinationKind.Mimic:
                if (Someone(mob) is not { } speaker)
                {
                    Show(session, mob, WFLatticeHallucinationKind.Radio);
                    return;
                }

                var line = Loc.GetString($"wf-lattice-mimic-{_random.Next(1, MimicLines + 1)}", ("name", Name(mob)));
                var said = Loc.GetString("wf-lattice-mimic-wrap", ("name", Name(speaker)), ("message", line));
                _chat.ChatMessageToOne(ChatChannel.Local, line, said, EntityUid.Invalid, false, session.Channel);
                return;
            default:
                ShowLie(session, mob, kind, seed);
                return;
        }
    }

    /// <summary>Another living person this one can see close by, if any.</summary>
    private EntityUid? Someone(EntityUid mob)
    {
        var near = new List<EntityUid>();
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        var at = _transform.GetMapCoordinates(mob);
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (uid == mob || xform.MapID != at.MapId || HasComp<GhostComponent>(uid) || !_mobState.IsAlive(uid)
                || !_examine.InRangeUnOccluded(mob, uid, NearRange))
                continue;

            near.Add(uid);
        }

        return near.Count == 0 ? null : _random.Pick(near);
    }

    /// <summary>Rolls whether the Unmooring takes one of a ship's small animals this tick.</summary>
    public void RollTake(EntityUid grid, int band, float depth, WFLatticeProfilePrototype profile, float seconds)
    {
        if (band < profile.TakeBand || profile.TakeChance <= 0f
            || TryComp<WFLatticeShelterComponent>(grid, out var shelter) && shelter.StopsTaking)
            return;

        if (!_random.Prob(profile.TakeChance * (1f + depth) * seconds / 60f))
            return;

        TryTake(grid, profile.TakeMaxMass);
    }

    /// <summary>Kills one living small animal aboard and starts it turning. False if there was none.</summary>
    public bool TryTake(EntityUid grid, float maxMass)
    {
        _animals.Clear();
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var state, out var xform))
        {
            if (xform.GridUid != grid
                || state.CurrentState != MobState.Alive
                || !_conversion.WouldTurnIfKilled(uid)
                || TryComp<PhysicsComponent>(uid, out var physics) && physics.FixturesMass > maxMass)
                continue;

            _animals.Add(uid);
        }

        foreach (var animal in _animals.OrderBy(_ => _random.Next()))
        {
            _mobState.ChangeMobState(animal, MobState.Dead);
            if (_conversion.TryStartConversion(animal))
                return true;
        }

        return false;
    }

    private void Radio(ICommonSession session, string name, string message)
    {
        var wrapped = Loc.GetString("wf-lattice-radio-wrap", ("color", RadioColor), ("name", name), ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Radio, message, wrapped, EntityUid.Invalid, false, session.Channel);
    }

    /// <summary>A random point round a mob, in its grid's frame; in open space if asked or if it has no grid.</summary>
    private EntityCoordinates Near(EntityUid mob, float min, float max, bool openSpace = false)
    {
        var xform = Transform(mob);
        var offset = _random.NextAngle().ToVec() * _random.NextFloat(min, max);
        if (openSpace || xform.GridUid is not { } grid)
        {
            var map = xform.MapUid ?? EntityUid.Invalid;
            return new EntityCoordinates(map, _transform.GetWorldPosition(xform) + offset);
        }

        return new EntityCoordinates(grid, Vector2.Transform(_transform.GetWorldPosition(xform) + offset, _transform.GetInvWorldMatrix(grid)));
    }
}
