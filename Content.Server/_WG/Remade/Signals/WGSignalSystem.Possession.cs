using Content.Server._WG.Remade.Abilities;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Director;
using Content.Server._WG.Remade.Elites;
using Content.Server._WG.Remade.Monolith;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WG.Remade.Signals;
using Content.Shared.Database;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Remade.Signals;

/// <summary>
/// A Signal driving a Remade. The Remade's thinking is switched off and the Director lets it go; the player gets its abilities as
/// actions. The Signal stays where it was with the player's mind still in it, and the player only visits the Remade, so when the
/// Remade is released, dies or is gone the player is back in the Signal.
/// </summary>
public sealed partial class WGSignalSystem
{
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private WGAbilitySystem _abilities = default!;

    private void InitializePossession()
    {
        SubscribeLocalEvent<WGSignalComponent, WGSignalPossessEvent>(OnPossess);
        SubscribeLocalEvent<WGSignalPossessedComponent, WGSignalReleaseEvent>(OnRelease);
        SubscribeLocalEvent<WGSignalPossessedComponent, WGSignalAbilityEvent>(OnAbility);
        SubscribeLocalEvent<WGSignalPossessedComponent, MobStateChangedEvent>(OnPossessedState);
        SubscribeLocalEvent<WGSignalPossessedComponent, PlayerDetachedEvent>(OnPossessedDetached);
        SubscribeLocalEvent<WGSignalPossessedComponent, EntityTerminatingEvent>(OnPossessedTerminating);
    }

    private void OnPossess(EntityUid uid, WGSignalComponent signal, WGSignalPossessEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryPossess((uid, signal), args.Target);
    }

    private void OnRelease(EntityUid uid, WGSignalPossessedComponent possessed, WGSignalReleaseEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Release(uid);
    }

    private void OnPossessedState(EntityUid uid, WGSignalPossessedComponent possessed, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Release(uid);
    }

    /// <summary>The player left the Remade without letting go, such as by disconnecting: the Remade is let go and they are back in the Signal.</summary>
    private void OnPossessedDetached(EntityUid uid, WGSignalPossessedComponent possessed, PlayerDetachedEvent args)
    {
        Release(uid);
    }

    private void OnPossessedTerminating(EntityUid uid, WGSignalPossessedComponent possessed, ref EntityTerminatingEvent args)
    {
        Release(uid);
    }

    /// <summary>Whether a Remade is one a Signal may take: its stone's, up and about, with nobody in it and nothing the Director needs it for.</summary>
    public bool CanPossess(Entity<WGSignalComponent> ent, EntityUid remade)
    {
        if (TerminatingOrDeleted(remade)
            || !TryComp<WGRemadeComponent>(remade, out var comp)
            || comp.Source != ent.Comp.Stone
            || !_mobState.IsAlive(remade)
            || HasComp<ActorComponent>(remade)
            || HasComp<WGSignalPossessedComponent>(remade)
            || HasComp<WGUndyingComponent>(remade)
            || HasComp<WGRemadePlayingDeadComponent>(remade)
            || HasComp<WGRemadeVentTripComponent>(remade))
        {
            return false;
        }

        // An Infector off building is the Director's work in progress.
        return !TryComp<WGRemadeBehaviourComponent>(remade, out var behaviour)
               || behaviour.Mode is not (WGRemadeMode.Hidden or WGRemadeMode.Build);
    }

    /// <summary>Takes over a Remade within reach, for Psi, if the Signal's player is there to drive it.</summary>
    public bool TryPossess(Entity<WGSignalComponent> ent, EntityUid remade)
    {
        var now = _timing.CurTime;
        if (!Bound(ent, out var stone) || TerminatingOrDeleted(remade) || !_mind.TryGetMind(ent, out var mindId, out var mind))
            return false;

        if (ent.Comp.Possessing != null)
            return Fail(ent, "wg-signal-busy");

        if (now < ent.Comp.PossessReady)
            return Fail(ent, "wg-signal-cooldown");

        if (ent.Comp.Psi < WGSignalMath.PossessCost)
            return Fail(ent, "wg-signal-no-psi");

        if (_corruption.IsSanctuaryAt(ent) || _corruption.IsSanctuaryAt(remade))
            return Fail(ent, "wg-signal-sanctuary");

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(remade);
        if (here.MapId != there.MapId || (here.Position - there.Position).Length() > WGSignalMath.PossessRange)
            return Fail(ent, "wg-signal-far");

        if (!CanPossess(ent, remade))
            return Fail(ent, "wg-signal-not-yours");

        Take(remade, ent);
        EnsureComp<MindContainerComponent>(remade);
        _mind.Visit(mindId, remade, mind);
        if (mind.VisitingEntity != remade)
        {
            Release(remade);
            return Fail(ent, "wg-signal-nothing");
        }

        Grant(remade, ent);
        ent.Comp.Possessing = remade;
        ent.Comp.Psi -= WGSignalMath.PossessCost;
        ent.Comp.PossessReady = now + TimeSpan.FromSeconds(WGSignalMath.PossessCooldown);
        ent.Comp.Possessions++;
        Dirty(ent);

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(ent):player} took over {ToPrettyString(remade):target}");
        return true;
    }

    /// <summary>Switches off a Remade's own thinking and takes it out of the Director's hands.</summary>
    private void Take(EntityUid remade, EntityUid signal)
    {
        EnsureComp<WGSignalPossessedComponent>(remade).Signal = signal;

        // With no behaviour the Director neither steers nor listens for it. A stalker's narrowed sight goes back first.
        if (TryComp<WGRemadeBehaviourComponent>(remade, out var behaviour))
        {
            if (behaviour.Sight is { } sight)
            {
                _npc.SetBlackboard(remade, "VisionRadius", sight.Vision);
                _npc.SetBlackboard(remade, "AggroVisionRadius", sight.Aggro);
            }

            RemComp<WGRemadeBehaviourComponent>(remade);
        }

        if (TryComp<HTNComponent>(remade, out var htn))
            _htn.SetHTNEnabled((remade, htn), false);

        _npc.SleepNPC(remade);
        if (TryComp<NPCSteeringComponent>(remade, out var steering))
            _steering.Unregister(remade, steering);

        if (TryComp<WGRemadeAbilitiesComponent>(remade, out var abilities))
            _abilities.StopActive((remade, abilities));
    }

    /// <summary>Gives the player the Remade's abilities as actions, and a way out.</summary>
    private void Grant(EntityUid remade, Entity<WGSignalComponent> signal)
    {
        var possessed = Comp<WGSignalPossessedComponent>(remade);
        if (TryComp<WGRemadeAbilitiesComponent>(remade, out var abilities))
        {
            var seen = new HashSet<string>();
            foreach (var ability in abilities.Abilities)
            {
                var name = ability.GetType().Name;
                if (!seen.Add(name) || !_proto.HasIndex<EntityPrototype>(ActionFor(name)))
                    continue;

                EntityUid? action = null;
                if (!_actions.AddAction(remade, ref action, ActionFor(name)))
                    continue;

                _actions.SetUseDelay(action, TimeSpan.FromSeconds(ability.Cooldown));
                possessed.Actions.Add(action.Value);
            }
        }

        EntityUid? release = null;
        if (_actions.AddAction(remade, ref release, ReleaseAction))
            possessed.Actions.Add(release.Value);
    }

    /// <summary>The action prototype for an ability class: WGLeapAbility is WGSignalActionLeap.</summary>
    public static string ActionFor(string abilityName)
    {
        var name = abilityName;
        if (name.StartsWith("WG", StringComparison.Ordinal))
            name = name[2..];

        if (name.EndsWith("Ability", StringComparison.Ordinal))
            name = name[..^"Ability".Length];

        return $"WGSignalAction{name}";
    }

    /// <summary>Lets go of a Remade: the player is back in the Signal, and the Remade thinks for itself again and answers to the Director.</summary>
    public void Release(EntityUid remade)
    {
        if (!TryComp<WGSignalPossessedComponent>(remade, out var possessed))
            return;

        var signal = possessed.Signal;
        foreach (var action in possessed.Actions)
        {
            if (TerminatingOrDeleted(action))
                continue;

            _actions.RemoveAction(remade, action);
            QueueDel(action);
        }

        RemComp<WGSignalPossessedComponent>(remade);

        if (!TerminatingOrDeleted(signal) && _mind.TryGetMind(signal, out var mindId, out var mind) && mind.VisitingEntity == remade)
            _mind.UnVisit(mindId, mind);

        if (!TerminatingOrDeleted(signal) && TryComp<WGSignalComponent>(signal, out var comp))
        {
            comp.Possessing = null;
            if (!TerminatingOrDeleted(remade))
                _transform.SetMapCoordinates(signal, _transform.GetMapCoordinates(remade));

            Dirty(signal, comp);
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(signal):player} let go of {ToPrettyString(remade):target}");
        }

        if (TerminatingOrDeleted(remade))
            return;

        if (TryComp<WGRemadeComponent>(remade, out var remadeComp))
            _director.Enlist(remade, remadeComp.Source);

        if (TryComp<HTNComponent>(remade, out var htn))
            _htn.SetHTNEnabled((remade, htn), true);

        if (_mobState.IsAlive(remade))
            _npc.WakeNPC(remade);
    }

    /// <summary>The ability the action stands for, aimed at the nearest living thing to the point or in reach.</summary>
    private void OnAbility(EntityUid uid, WGSignalPossessedComponent possessed, WGSignalAbilityEvent args)
    {
        if (args.Handled || !TryComp<WGRemadeAbilitiesComponent>(uid, out var abilities) || _mobState.IsDead(uid))
            return;

        WGAbility? ability = null;
        foreach (var candidate in abilities.Abilities)
        {
            if (candidate.GetType().Name == args.Ability)
            {
                ability = candidate;
                break;
            }
        }

        if (ability == null)
            return;

        if (_corruption.IsSanctuaryAt(uid))
        {
            Refuse(uid, "wg-signal-sanctuary");
            return;
        }

        var target = PickTarget(uid, args.Target, ability);
        if (!_abilities.TryUse((uid, abilities), ability, target))
        {
            Refuse(uid, "wg-signal-ability-none");
            return;
        }

        possessed.Target = target;
        args.Handled = true;
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(possessed.Signal):player} used {args.Ability} with {ToPrettyString(uid):entity} at {ToPrettyString(target):target}");
    }

    /// <summary>The living thing nearest the point that was clicked, else the nearest in reach of the ability, else the Remade itself.</summary>
    private EntityUid PickTarget(EntityUid remade, EntityCoordinates click, WGAbility ability)
    {
        var point = _transform.ToMapCoordinates(click).Position;
        var here = _transform.GetWorldPosition(remade);
        EntityUid? best = null;
        var bestDistance = 3f;
        EntityUid? nearest = null;
        var nearestDistance = ability.Range;
        foreach (var prey in _abilities.Prey(remade, ability.Range + 3f))
        {
            var position = _transform.GetWorldPosition(prey);
            var fromPoint = (position - point).Length();
            if (fromPoint < bestDistance)
            {
                best = prey;
                bestDistance = fromPoint;
            }

            var fromHere = (position - here).Length();
            if (fromHere < nearestDistance)
            {
                nearest = prey;
                nearestDistance = fromHere;
            }
        }

        return best ?? nearest ?? remade;
    }

    private void UpdateDriven(float frameTime)
    {
        var query = EntityQueryEnumerator<WGSignalPossessedComponent, WGRemadeAbilitiesComponent>();
        while (query.MoveNext(out var uid, out var possessed, out var abilities))
        {
            _abilities.UpdateDriven((uid, abilities), possessed.Target, frameTime);
        }
    }
}
