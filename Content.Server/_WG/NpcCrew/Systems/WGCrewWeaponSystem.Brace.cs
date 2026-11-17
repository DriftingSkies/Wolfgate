using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Interaction;
using Content.Shared.NPC.Components;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>What a crewman does about one hostile.</summary>
public enum WGCrewEngage : byte
{
    /// <summary>Leave it be.</summary>
    No,

    /// <summary>Fight it: shoot it in sight, or go after it.</summary>
    Engage,

    /// <summary>Stay drawn where he is and cover the approach to it, unseen.</summary>
    Brace,
}

/// <summary>
/// Bracing: a crewman who knows of a hostile aboard but may not go after it keeps his weapon up and covers the way it
/// would come, and remembers what he has seen so that a target he expects is fired on at once.
/// </summary>
public sealed partial class WGCrewWeaponSystem
{
    [Dependency] private RotateToFaceSystem _rotateToFace = default!;

    /// <summary>The longest a crewman covers the approach to one hostile he never gets sight of.</summary>
    private static readonly TimeSpan BraceLimit = TimeSpan.FromSeconds(90);

    /// <summary>How long a brace holds without being renewed by the brace task.</summary>
    private static readonly TimeSpan BraceLease = TimeSpan.FromSeconds(2);

    /// <summary>How long after losing sight of a target a crewman still expects it.</summary>
    private static readonly TimeSpan SightMemory = TimeSpan.FromSeconds(6);

    /// <summary>How long sightings and notices are kept for a hostile no longer in his attention.</summary>
    private static readonly TimeSpan ContactMemory = TimeSpan.FromSeconds(60);

    /// <summary>How often a brace checks it still holds.</summary>
    private static readonly TimeSpan BraceCheckGap = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<EntityUid, (TimeSpan Until, EntityUid? Target)> _braces = new();

    /// <summary>Whether this crewman braces for a hostile instead of letting it be: his skill braces, he holds no station, and he has not covered it in vain for too long.</summary>
    private bool MayBrace(EntityUid uid, WGCrewComponent crew, EntityUid target)
    {
        if (!WGCrewSkills.Of(crew).Braces || IsStationCrew(uid) || !TryComp<WGCrewWeaponComponent>(uid, out var weapon))
            return false;

        if (weapon.Braced != target)
            return true;

        var since = weapon.BraceSince;
        if (weapon.Sighted.TryGetValue(target, out var seen) && seen > since)
            since = seen;
        return _timing.CurTime < since + BraceLimit;
    }

    /// <summary>The hostile this crewman braces for, cached briefly like <see cref="HasLiveThreat"/>.</summary>
    public EntityUid? BraceTarget(EntityUid uid)
    {
        var now = _timing.CurTime;
        if (_braces.TryGetValue(uid, out var cached) && now < cached.Until)
            return cached.Target;

        var target = PickBraceTarget(uid);
        _braces[uid] = (now + ThreatCacheTime * (1 + _random.NextFloat()), target);
        return target;
    }

    /// <summary>
    /// Whether the crewman's engagement rule lets him take part in a fight now: never for Never, always for on-sight
    /// crew, and for when-attacked crew only while they remember an attack.
    /// </summary>
    public bool Provoked(EntityUid uid)
    {
        if (!TryComp<WGCrewComponent>(uid, out var crew))
            return true;
        if (crew.Engagement == WGCrewEngagement.Never)
            return false;
        if (crew.Engagement == WGCrewEngagement.OnSight)
            return true;

        if (TryComp<NPCRetaliationComponent>(uid, out var retaliation) && retaliation.AttackMemoryLength != null)
        {
            foreach (var expiry in retaliation.AttackMemories.Values)
            {
                if (_timing.CurTime < expiry)
                    return true;
            }

            return false;
        }

        // No memory window: provoked for as long as someone is on the aggro list.
        return TryComp<FactionExceptionComponent>(uid, out var exceptions) && exceptions.Hostiles.Count > 0;
    }

    /// <summary>Whether a mob is this crewman's enemy: hostile to his faction, or someone he remembers attacking him.</summary>
    public bool IsEnemy(EntityUid uid, EntityUid target)
    {
        return IsHostile(uid, CompOrNull<NpcFactionMemberComponent>(uid), target) || RecentlyAttackedBy(uid, target);
    }

    /// <summary>Starts or renews the crewman's brace on a hostile, keeping when it started if it is the same one.</summary>
    public void Brace(Entity<WGCrewWeaponComponent> ent, EntityUid target)
    {
        var now = _timing.CurTime;
        if (ent.Comp.Braced != target || now >= ent.Comp.BracedUntil + ContactMemory)
            ent.Comp.BraceSince = now;
        ent.Comp.Braced = target;
        ent.Comp.BracedUntil = now + BraceLease;
        ent.Comp.Noticed.TryAdd(target, now);
    }

    /// <summary>
    /// Renews a brace, facing the hostile's way. False once the crewman should stop: it can be fought now, or no
    /// longer needs covering.
    /// </summary>
    public bool HoldBrace(Entity<WGCrewWeaponComponent> ent, EntityUid target)
    {
        var now = _timing.CurTime;
        if (now >= ent.Comp.NextBraceCheck)
        {
            ent.Comp.NextBraceCheck = now + BraceCheckGap;
            if (Assess(ent, target) != WGCrewEngage.Brace || !Provoked(ent))
                return false;
        }

        Brace(ent, target);
        if (!TerminatingOrDeleted(target))
            _rotateToFace.TryFaceCoordinates(ent, _xformSystem.GetWorldPosition(target));
        return true;
    }

    /// <summary>Whether the crewman is covering the approach to a hostile right now.</summary>
    public bool IsBracing(Entity<WGCrewWeaponComponent> ent)
    {
        return ent.Comp.Braced != null && _timing.CurTime < ent.Comp.BracedUntil;
    }

    /// <summary>Whether the crewman expects this target: seen moments ago, or braced for.</summary>
    public bool Expects(Entity<WGCrewWeaponComponent> ent, EntityUid target)
    {
        var now = _timing.CurTime;
        return ent.Comp.Braced == target && now < ent.Comp.BracedUntil
               || ent.Comp.Sighted.TryGetValue(target, out var seen) && now < seen + SightMemory;
    }

    /// <summary>Notes that the crewman has the target in sight.</summary>
    public void Saw(Entity<WGCrewWeaponComponent> ent, EntityUid target)
    {
        var now = _timing.CurTime;
        ent.Comp.Sighted[target] = now;
        ent.Comp.Noticed.TryAdd(target, now);
    }

    /// <summary>How long the crewman has been after or braced for a hostile without seeing it.</summary>
    public TimeSpan UnseenFor(Entity<WGCrewWeaponComponent> ent, EntityUid target)
    {
        if (!ent.Comp.Noticed.TryGetValue(target, out var since))
            return TimeSpan.Zero;
        if (ent.Comp.Sighted.TryGetValue(target, out var seen) && seen > since)
            since = seen;
        return _timing.CurTime - since;
    }

    /// <summary>The hostile the crewman is after or braced for, if any.</summary>
    public EntityUid? Focus(Entity<WGCrewWeaponComponent> ent, HTNComponent? plan)
    {
        if (plan != null && plan.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager))
            return target;
        return IsBracing(ent) ? ent.Comp.Braced : null;
    }

    /// <summary>Keeps the crewman's sightings of his focus current and forgets old ones.</summary>
    private void Track(Entity<WGCrewWeaponComponent> ent, HTNComponent? plan, TimeSpan now)
    {
        var focus = Focus(ent, plan);
        if (focus is { } target && !TerminatingOrDeleted(target))
        {
            ent.Comp.Noticed.TryAdd(target, now);
            if (CanSee(ent, target))
                ent.Comp.Sighted[target] = now;
        }

        Forget(ent.Comp.Sighted, focus, now);
        Forget(ent.Comp.Noticed, focus, now);
    }

    private void Forget(Dictionary<EntityUid, TimeSpan> contacts, EntityUid? keep, TimeSpan now)
    {
        foreach (var (target, at) in contacts)
        {
            if (target != keep && (now >= at + ContactMemory || TerminatingOrDeleted(target)))
                contacts.Remove(target);
        }
    }
}
