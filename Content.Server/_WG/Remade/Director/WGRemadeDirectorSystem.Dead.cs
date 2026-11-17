using Content.Shared._WG.Remade;
using System.Linq;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Monolith;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WG.Remade.Director;

/// <summary>Playing dead. Some Remade lie among the corpses with a dead body's sprite and rise when crew come near or hit them.</summary>
public sealed partial class WGRemadeDirectorSystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private const float AmbushMin = 5f;
    private const float AmbushMax = 26f;
    private const float AmbushBodySpacing = 2.5f;
    private static readonly TimeSpan DeadRate = TimeSpan.FromSeconds(0.25);

    private readonly List<(EntityUid Remade, EntityUid Crewman)> _rising = new();
    private TimeSpan _nextDead;

    private void InitializeDead()
    {
        SubscribeLocalEvent<WGRemadePlayingDeadComponent, DamageChangedEvent>(OnDeadDamaged);
    }

    private void OnDeadDamaged(Entity<WGRemadePlayingDeadComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            Rise(ent, args.Origin);
    }

    /// <summary>Makes a Remade lie still, looking dead. It rises when a living crewman comes within a few steps or it is hurt.</summary>
    public void PlayDead(EntityUid remade)
    {
        if (HasComp<WGRemadePlayingDeadComponent>(remade))
            return;

        AddComp<WGRemadePlayingDeadComponent>(remade);
        EnsureComp<WGRemadeAppearsDeadComponent>(remade);
        Freeze(remade);
        var behaviour = EnsureComp<WGRemadeBehaviourComponent>(remade);
        behaviour.Mode = WGRemadeMode.Hunt;
        ClearKnown(remade, behaviour);
        _appearance.SetData(remade, MobStateVisuals.State, MobState.Dead);
    }

    /// <summary>A Remade that was playing dead gets up and comes for whoever woke it.</summary>
    private void Rise(EntityUid remade, EntityUid? cause)
    {
        if (!RemComp<WGRemadePlayingDeadComponent>(remade))
            return;

        RemComp<WGRemadeAppearsDeadComponent>(remade);

        if (!_mobState.IsDead(remade))
            _appearance.SetData(remade, MobStateVisuals.State, MobState.Alive);

        Thaw(remade);
        Unseal(remade);
        if (TryComp<WGRemadeVoiceComponent>(remade, out var voice))
            _voice.Shout((remade, voice));

        if (cause != null && !TerminatingOrDeleted(cause.Value) && TryComp<WGRemadeBehaviourComponent>(remade, out var behaviour))
            SetKnown(remade, behaviour, Transform(cause.Value).Coordinates, _timing.CurTime);
    }

    private void UpdateDead(TimeSpan now)
    {
        if (now < _nextDead)
            return;

        _nextDead = now + DeadRate;
        _rising.Clear();
        var near = new HashSet<Entity<ActorComponent>>();
        var query = EntityQueryEnumerator<WGRemadePlayingDeadComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var dead, out var xform))
        {
            near.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, dead.WakeRadius, near);
            foreach (var crewman in near)
            {
                if (!HasComp<WGRemadeComponent>(crewman) && !_mobState.IsDead(crewman))
                {
                    _rising.Add((uid, crewman));
                    break;
                }
            }
        }

        foreach (var (remade, crewman) in _rising)
        {
            Rise(remade, crewman);
        }
    }

    private static int MaxDead(WGMonolithStage stage)
    {
        return stage switch
        {
            WGMonolithStage.Growth => 3,
            WGMonolithStage.Harvest => 5,
            WGMonolithStage.Rapture => 8,
            WGMonolithStage.Crescendo => 10,
            _ => 0,
        };
    }

    private int CountDead(WGRemadeDirectorComponent director)
    {
        return director.Horde.Count(uid => HasComp<WGRemadePlayingDeadComponent>(uid));
    }

    /// <summary>Lays a Remade among the dead near a crewman, or on corruption when there are none, to be found the hard way.</summary>
    private void TryAmbush(Entity<WGRemadeDirectorComponent, WGMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;
        if (CountDead(director) >= MaxDead(stage) || director.Horde.Count >= HordeCap(stage))
            return;

        if (PickFocus(director, now) is not { } focus || StressOf(focus) >= director.StressHigh)
            return;

        var xform = Transform(focus);
        var here = _transform.GetWorldPosition(xform);
        var sites = new List<EntityCoordinates>();

        var bodies = new HashSet<Entity<MobStateComponent>>();
        _lookup.GetEntitiesInRange(xform.Coordinates, AmbushMax, bodies);
        var lying = new HashSet<Entity<WGRemadePlayingDeadComponent>>();
        foreach (var body in bodies)
        {
            if (!_mobState.IsDead(body) || HasComp<WGRemadeComponent>(body) || Transform(body).GridUid != xform.GridUid)
                continue;

            var distance = (_transform.GetWorldPosition(body) - here).Length();
            if (distance < AmbushMin)
                continue;

            lying.Clear();
            _lookup.GetEntitiesInRange(Transform(body).Coordinates, AmbushBodySpacing, lying);
            if (lying.Count == 0)
                sites.Add(Transform(body).Coordinates.Offset(_random.NextAngle().ToVec() * 0.7f));
        }

        if (sites.Count == 0)
        {
            foreach (var uid in ent.Comp2.Corruption)
            {
                if (!TryComp(uid, out TransformComponent? corruption) || corruption.GridUid != xform.GridUid)
                    continue;

                var distance = (_transform.GetWorldPosition(corruption) - here).Length();
                if (distance is >= AmbushMin and <= AmbushMax)
                    sites.Add(corruption.Coordinates);
            }
        }

        // Trays and nests are the best places to lie, since every body there is a question.
        var fakes = FakeSites(focus, xform.GridUid, AmbushMin, AmbushMax);
        if (sites.Count == 0 && fakes.Count == 0 || Buy(ent, focus, false) is not { } proto)
            return;

        var useFake = fakes.Count > 0 && (sites.Count == 0 || _random.Prob(0.6f));
        var site = useFake ? _random.Pick(fakes) : new FakeSite(_random.Pick(sites), null);
        var remade = Spawn(proto, site.At);
        Enlist(remade, ent.Owner);
        PlayDead(remade);
        if (site.Tray is { } tray)
            HideInTray(remade, tray);
    }
}
