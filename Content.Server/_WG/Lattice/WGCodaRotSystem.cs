using Content.Shared._WG.Lattice;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Lattice;

/// <summary>Tuning for the Coda's rot: what open space out there does to flesh.</summary>
[CVarDefs]
public sealed class WGCodaRotCVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.lattice.coda_rot", true, CVar.SERVERONLY);

    /// <summary>Seconds out in the open before flesh starts coming apart.</summary>
    public static readonly CVarDef<float> Grace =
        CVarDef.Create("wg.lattice.coda_rot_grace", 20f, CVar.SERVERONLY);

    /// <summary>Cellular damage a second once it starts; no suit stops it, an anchor charm halves it.</summary>
    public static readonly CVarDef<float> Damage =
        CVarDef.Create("wg.lattice.coda_rot_damage", 2f, CVar.SERVERONLY);

    /// <summary>Seconds out in the open before an arm twists into a blade.</summary>
    public static readonly CVarDef<float> Mutate =
        CVarDef.Create("wg.lattice.coda_rot_mutate", 45f, CVar.SERVERONLY);
}

/// <summary>How long a person has been out in the Coda's open space, and the warnings they have had.</summary>
[RegisterComponent]
public sealed partial class WGCodaRotComponent : Component
{
    [ViewVariables]
    public float Exposure;

    /// <summary>The last warning given, 0 for none.</summary>
    [ViewVariables]
    public int Warned;

    [ViewVariables]
    public TimeSpan NextReminder;
}

/// <summary>
/// The Coda eats anyone out in its open space, off any hull: a prickle, then something moving under the skin, then flesh
/// coming apart that no suit keeps out, and an arm twisting into a blade. Whoever dies out there rises Remade. Back aboard,
/// it stops. The Choir are at home in it.
/// </summary>
public sealed class WGCodaRotSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCodaMutationSystem _mutation = default!;
    [Dependency] private WGDriftEffectsSystem _effects = default!;
    [Dependency] private WGLatticeSystem _lattice = default!;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReminderGap = TimeSpan.FromSeconds(15);
    private static readonly ProtoId<DamageTypePrototype> Cellular = "Cellular";
    private static readonly SoundSpecifier WarnSound = new SoundPathSpecifier("/Audio/_WG/Lattice/drift_pulse.ogg");

    /// <summary>Exposure, in seconds, falls this much faster than it builds once aboard.</summary>
    private const float Recovery = 3f;

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        if (!_config.GetCVar(WGCodaRotCVars.Enabled))
            return;

        var seconds = (float) Tick.TotalSeconds;
        var grace = _config.GetCVar(WGCodaRotCVars.Grace);
        var mutateAt = _config.GetCVar(WGCodaRotCVars.Mutate);
        var rate = _config.GetCVar(WGCodaRotCVars.Damage);

        var people = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (people.MoveNext(out var uid, out var actor, out var xform))
        {
            var exposed = Exposed(uid, xform, out var profile);
            var rot = CompOrNull<WGCodaRotComponent>(uid);
            if (!exposed)
            {
                if (rot == null)
                    continue;

                rot.Exposure -= seconds * Recovery;
                if (rot.Exposure <= 0f)
                    RemComp<WGCodaRotComponent>(uid);
                continue;
            }

            rot ??= AddComp<WGCodaRotComponent>(uid);
            rot.Exposure += seconds;
            Warn((uid, rot), actor.PlayerSession, grace, now);

            if (rot.Exposure < grace)
                continue;

            var share = TryComp<WGLatticeShelterComponent>(uid, out var charm) && charm.StopsTaking ? 0.5f : 1f;
            var damage = new DamageSpecifier();
            damage.DamageDict[Cellular] = rate * share * seconds;
            _damage.TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false);

            if (rot.Exposure >= mutateAt && profile != null && !HasComp<WGCodaMutatedComponent>(uid))
                _mutation.Mutate(uid, profile);
        }
    }

    /// <summary>Whether a living, non-Choir person is out in the Coda's open space, off any hull.</summary>
    private bool Exposed(EntityUid uid, TransformComponent xform, out WGLatticeProfilePrototype? profile)
    {
        profile = null;
        if (xform.GridUid != null || xform.MapUid is not { } map
            || !_mobs.IsAlive(uid)
            || HasComp<WGChoirMemberComponent>(uid)
            || !TryComp<WGLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out profile))
            return false;

        return _lattice.Locate(map, _transform.GetWorldPosition(xform)).Band >= profile.HazardBand;
    }

    /// <summary>The warnings: on going out, as it gets under the skin, when it starts, and every so often after.</summary>
    private void Warn(Entity<WGCodaRotComponent> ent, ICommonSession session, float grace, TimeSpan now)
    {
        var stage = ent.Comp.Exposure >= grace ? 3 : ent.Comp.Exposure >= grace / 2f ? 2 : 1;
        if (stage > ent.Comp.Warned)
        {
            ent.Comp.Warned = stage;
            ent.Comp.NextReminder = now + ReminderGap;
            Tell(ent, session, $"wg-coda-rot-{stage}");
            return;
        }

        if (stage == 3 && now >= ent.Comp.NextReminder)
        {
            ent.Comp.NextReminder = now + ReminderGap;
            Tell(ent, session, "wg-coda-rot-reminder");
        }
    }

    private void Tell(EntityUid uid, ICommonSession session, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), uid, uid, PopupType.LargeCaution);
        _audio.PlayEntity(WarnSound, Filter.SinglePlayer(session), uid, true);
        _effects.Show(session, uid, WGLatticeHallucinationKind.Pulse);
    }
}
