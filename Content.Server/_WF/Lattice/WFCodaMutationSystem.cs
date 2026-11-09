using System.Linq;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Lattice;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// Deep in the Coda, at high Drift and unsheltered, a person's arm can twist into a Remade's blade: held in that hand and
/// never let go. It withers away once they have been out of the Coda long enough.
/// </summary>
public sealed class WFCodaMutationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFDriftMindSystem _mind = default!;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);

    /// <summary>Seconds out of the Coda before a mutation withers, off any map with a Lattice profile.</summary>
    private const float FallbackWither = 600f;
    private static readonly SoundSpecifier TwistSound = new SoundPathSpecifier("/Audio/Effects/gib2.ogg");

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTwistedArmRemovalComponent, SurgeryStepEvent>(OnRemoved);
    }

    /// <summary>A surgeon cuts the twisted arm away; what's left is Remade tissue, worth something to the right buyer.</summary>
    private void OnRemoved(Entity<WFTwistedArmRemovalComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp<WFCodaMutatedComponent>(args.Body, out var mutated))
            return;

        if (!TerminatingOrDeleted(mutated.Blade))
            Del(mutated.Blade);

        RemComp<WFCodaMutatedComponent>(args.Body);
        Spawn(ent.Comp.Tissue, Transform(args.Body).Coordinates);
        _popup.PopupEntity(Loc.GetString("wf-coda-mutate-removed"), args.Body, args.Body, PopupType.Medium);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        var seconds = (float) Tick.TotalSeconds;
        Wither(now);

        var people = EntityQueryEnumerator<ActorComponent, HandsComponent, TransformComponent>();
        while (people.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (HasComp<WFCodaMutatedComponent>(uid)
                || !_mind.CanDrift(uid)
                || Profile(xform.MapUid) is not { } profile
                || Exposure(uid, xform) is not { } exposure
                || exposure.Band < profile.MutationBand
                || exposure.Drift < profile.MutationMinDrift
                || exposure.Sheltered)
                continue;

            var share = Math.Clamp((exposure.Drift - profile.MutationMinDrift) / Math.Max(1f, profile.DriftMax - profile.MutationMinDrift), 0f, 1f);
            if (_random.Prob(profile.MutationChance * (1f + share) * seconds / 60f))
                Mutate(uid, profile);
        }
    }

    /// <summary>Twists one of a person's arms into a blade now. False if they have no hand for it.</summary>
    public bool Mutate(EntityUid mob, WFLatticeProfilePrototype profile)
    {
        if (!TryComp<HandsComponent>(mob, out var hands) || hands.Hands.Count == 0 || HasComp<WFCodaMutatedComponent>(mob))
            return false;

        var hand = _random.Pick(hands.Hands.Values.ToList());
        var held = hand.HeldEntity;
        if (held != null && !_hands.TryDrop(mob, hand, checkActionBlocker: false, handsComp: hands))
            return false;

        var blade = Spawn(profile.MutationArm, Transform(mob).Coordinates);
        if (!_hands.TryPickup(mob, blade, hand, checkActionBlocker: false, animate: false, handsComp: hands))
        {
            Del(blade);
            // Give back what they were holding, so a failed twist costs them nothing.
            if (held is { } item && !TerminatingOrDeleted(item))
                _hands.TryPickup(mob, item, hand, checkActionBlocker: false, animate: false, handsComp: hands);
            return false;
        }

        var mutated = EnsureComp<WFCodaMutatedComponent>(mob);
        mutated.Blade = blade;
        mutated.OutSince = null;
        Dirty(mob, mutated);
        _audio.PlayPvs(TwistSound, mob);
        _popup.PopupEntity(Loc.GetString("wf-coda-mutate"), mob, mob, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("wf-coda-mutate-others", ("name", mob)), mob, Filter.PvsExcept(mob), true, PopupType.MediumCaution);
        return true;
    }

    /// <summary>Lets go of mutations whose blade is gone, and withers those out of the Coda long enough.</summary>
    private void Wither(TimeSpan now)
    {
        var mutated = EntityQueryEnumerator<WFCodaMutatedComponent, TransformComponent>();
        while (mutated.MoveNext(out var uid, out var comp, out var xform))
        {
            if (TerminatingOrDeleted(comp.Blade))
            {
                RemCompDeferred<WFCodaMutatedComponent>(uid);
                continue;
            }

            var profile = Profile(xform.MapUid);
            var inside = profile != null && Exposure(uid, xform) is { } exposure && exposure.Band >= profile.MutationBand;
            if (inside || _mobState.IsDead(uid))
            {
                comp.OutSince = null;
                continue;
            }

            comp.OutSince ??= now;
            if (now - comp.OutSince.Value < TimeSpan.FromSeconds(profile?.MutationWither ?? FallbackWither))
                continue;

            Del(comp.Blade);
            RemCompDeferred<WFCodaMutatedComponent>(uid);
            _popup.PopupEntity(Loc.GetString("wf-coda-mutate-wither"), uid, uid, PopupType.Medium);
        }
    }

    private WFLatticeProfilePrototype? Profile(EntityUid? map)
    {
        return TryComp<WFLatticeFieldComponent>(map, out var field) && _proto.TryIndex(field.Profile, out var profile) ? profile : null;
    }

    /// <summary>The band a person is in, the Drift they hold and whether they are sheltered, from their ship or themselves.</summary>
    private (int Band, float Drift, bool Sheltered)? Exposure(EntityUid mob, TransformComponent xform)
    {
        var carrier = xform.GridUid is { } grid && HasComp<WFDriftComponent>(grid) ? grid : mob;
        if (!TryComp<WFDriftComponent>(carrier, out var drift))
            return null;

        var head = CompOrNull<WFDriftComponent>(mob);
        var felt = head?.FeltLevel > 0 ? head.Felt : drift.Drift;
        var own = head?.Mind ?? 0f;
        var sheltered = TryComp<WFLatticeShelterComponent>(carrier, out var shelter) && shelter.StopsTaking
                        || TryComp<WFLatticeShelterComponent>(mob, out var charm) && charm.StopsTaking;
        return (drift.Band, Math.Max(felt, own), sheltered);
    }
}
