using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC.Systems;
using Content.Shared.Chat;
using Content.Shared.Explosion.Components;
using Content.Shared.Explosion.Components.OnTrigger;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Where and when a crewman may throw a grenade. Set on the HTN task that throws it.</summary>
public readonly record struct WGCrewGrenadeRules(
    float BlastRadius,
    float HostileRange,
    float ThrowRange,
    float LandNear,
    float MinUnseen,
    float CrewCooldown,
    float MinFuse);

/// <summary>
/// Crew grenades: the answer to a hostile holding a corner. A crewman who knows where a hostile is but cannot see it
/// throws a grenade he carries to a spot he can see that has a clear line to the hostile, shouting first so the
/// player can react. He cooks it by his skill but never so long that less than a set fuse is left when it lands, never
/// throws with a crewmate or a bystander in its blast, and nobody goes after the hostile until it has gone off.
/// </summary>
public sealed partial class WGCrewGrenadeSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCCombatSystem _npcCombat = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;

    /// <summary>Lines shouted before a grenade that explodes.</summary>
    private static readonly LocId[] FragLines =
        { "wg-crew-grenade-frag-1", "wg-crew-grenade-frag-2", "wg-crew-grenade-frag-3" };

    /// <summary>Lines shouted before any other grenade.</summary>
    private static readonly LocId[] OtherLines = { "wg-crew-grenade-1", "wg-crew-grenade-2" };

    /// <summary>How long a crewman who found nowhere to throw waits before looking again.</summary>
    private static readonly TimeSpan RetryGap = TimeSpan.FromSeconds(2);

    /// <summary>How long after its throw time a grenade still in hand is thrown by the safety sweep.</summary>
    private static readonly TimeSpan ThrowGrace = TimeSpan.FromSeconds(0.5);

    /// <summary>When each crew, on each ship, may next throw.</summary>
    private readonly Dictionary<(EntityUid Grid, string Group), TimeSpan> _cooldowns = new();

    /// <summary>When each crewman may next look for somewhere to throw.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextTry = new();

    /// <summary>Mobs a throw must spare, reused across plans.</summary>
    private readonly HashSet<Entity<MobStateComponent>> _mobsNear = new();
    private readonly List<Vector2> _spared = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ =>
        {
            _cooldowns.Clear();
            _nextTry.Clear();
        });
    }

    /// <summary>
    /// Finds a grenade the crewman carries, the hostile to throw it at and where it should land. False when he
    /// may not throw now or there is nowhere safe that reaches the hostile.
    /// </summary>
    public bool TryPlan(EntityUid uid, EntityUid? hunted, WGCrewGrenadeRules rules,
        out EntityUid grenade, out EntityUid target, out EntityCoordinates at)
    {
        grenade = default;
        target = default;
        at = default;
        var now = _timing.CurTime;
        if (!TryComp<WGCrewComponent>(uid, out var crew) || !WGCrewSkills.Of(crew).Throws
            || HasComp<ActorComponent>(uid) || HasComp<WGCrewNonLethalComponent>(uid)
            || HasComp<WGCrewArmedGrenadeComponent>(uid) || !TryComp<WGCrewWeaponComponent>(uid, out var weapon)
            || _nextTry.TryGetValue(uid, out var next) && now < next
            || !_mobs.IsAlive(uid) || !_npcCombat.CanNpcShoot(uid)
            || Transform(uid).GridUid is not { } grid
            || _cooldowns.TryGetValue((grid, crew.Group), out var ready) && now < ready
            || !FindGrenade(uid, out var carried, out _) || !HasThrowingHand(uid, weapon))
            return false;

        Span<EntityUid?> candidates = stackalloc EntityUid?[] { hunted, _weapons.BraceTarget(uid) };
        foreach (var candidate in candidates)
        {
            if (candidate is not { } hostile || !Worth(uid, (uid, weapon), hostile, grid, rules)
                || !TryFindLanding(uid, hostile, carried.Value, rules, out at))
                continue;

            grenade = carried.Value;
            target = hostile;
            return true;
        }

        _nextTry[uid] = now + RetryGap;
        return false;
    }

    /// <summary>Whether a hostile is worth a grenade: aboard, near, still to be dealt with, and out of the crewman's sight for a while.</summary>
    private bool Worth(EntityUid uid, Entity<WGCrewWeaponComponent> weapon, EntityUid hostile, EntityUid grid, WGCrewGrenadeRules rules)
    {
        if (TerminatingOrDeleted(hostile) || !_mobs.IsAlive(hostile) || Transform(hostile).GridUid != grid
            || _weapons.BlastHeld(hostile) || _weapons.Assess(uid, hostile) == WGCrewEngage.No
            || _weapons.CanSee(uid, hostile, ignoreStealth: true)
            || _weapons.UnseenFor(weapon, hostile) < TimeSpan.FromSeconds(rules.MinUnseen))
            return false;

        var from = _xform.GetWorldPosition(uid);
        return Vector2.Distance(from, _xform.GetWorldPosition(hostile)) <= rules.HostileRange;
    }

    /// <summary>
    /// The tile nearest the hostile that the crewman can throw to and that has a clear line to the hostile, so the
    /// blast reaches round the corner the throw cannot. It must lie outside his own blast and spare everyone who is
    /// not his enemy, and leave at least <see cref="WGCrewGrenadeRules.MinFuse"/> on the fuse when it lands.
    /// </summary>
    private bool TryFindLanding(EntityUid uid, EntityUid hostile, EntityUid grenade, WGCrewGrenadeRules rules, out EntityCoordinates at,
        float? fuse = null)
    {
        at = default;
        if (TerminatingOrDeleted(hostile) || Transform(hostile).GridUid is not { } grid
            || !TryComp<MapGridComponent>(grid, out var gridComp)
            || !TryComp<OnUseTimerTriggerComponent>(grenade, out var timer))
            return false;

        var fuseLeft = fuse ?? timer.Delay;

        var from = _xform.GetMapCoordinates(uid);
        var there = _xform.GetMapCoordinates(hostile);
        var speed = CompOrNull<HandsComponent>(uid)?.BaseThrowspeed ?? 10f;
        Spared(uid, hostile, from, rules);

        var centre = _maps.TileIndicesFor(grid, gridComp, there);
        var reach = (int) MathF.Ceiling(rules.LandNear);
        var best = float.MaxValue;
        MapCoordinates? landing = null;
        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dy = -reach; dy <= reach; dy++)
            {
                var tile = centre + new Vector2i(dx, dy);
                var spot = _maps.GridTileToWorld(grid, gridComp, tile);
                var near = Vector2.Distance(spot.Position, there.Position);
                if (near > rules.LandNear || near >= best)
                    continue;

                var distance = Vector2.Distance(spot.Position, from.Position);
                if (distance > rules.ThrowRange || distance <= rules.BlastRadius
                    || fuseLeft - distance / speed < rules.MinFuse
                    || !_maps.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty
                    || _turf.IsTileBlocked(tileRef, CollisionGroup.Impassable)
                    || !ClearThrow(uid, from, spot) || !Clear(uid, spot, there)
                    || !SparesEveryone(from.Position, spot.Position, rules.BlastRadius))
                    continue;

                best = near;
                landing = spot;
            }
        }

        if (landing is not { } found)
            return false;

        at = _xform.ToCoordinates(grid, found);
        return true;
    }

    /// <summary>Whether a thrown grenade flies clear between two points: nothing it would hit, loose crates included.</summary>
    private bool ClearThrow(EntityUid uid, MapCoordinates a, MapCoordinates b)
    {
        // Mobs are left to Spared: the hostile may be struck, and crewmates are checked there.
        return _interaction.InRangeUnobstructed(a, b, Vector2.Distance(a.Position, b.Position) + 0.01f,
            CollisionGroup.ThrownItem, blocker => blocker == uid || HasComp<MobStateComponent>(blocker));
    }

    /// <summary>Whether nothing built into the ship stands between two points, for the blast's reach.</summary>
    private bool Clear(EntityUid uid, MapCoordinates a, MapCoordinates b)
    {
        return _interaction.InRangeUnobstructed(a, b, Vector2.Distance(a.Position, b.Position) + 0.01f,
            CollisionGroup.Impassable, blocker => blocker == uid || !Transform(blocker).Anchored);
    }

    /// <summary>Collects where everyone stands that a throw from here must not hurt: anyone not dead who is not the crewman's enemy.</summary>
    private void Spared(EntityUid uid, EntityUid hostile, MapCoordinates from, WGCrewGrenadeRules rules)
    {
        _spared.Clear();
        _mobsNear.Clear();
        _lookup.GetEntitiesInRange(from, rules.ThrowRange + rules.BlastRadius, _mobsNear);
        foreach (var mob in _mobsNear)
        {
            if (mob.Owner == uid || mob.Owner == hostile || _mobs.IsDead(mob.Owner, mob.Comp) || _weapons.IsEnemy(uid, mob.Owner))
                continue;
            _spared.Add(_xform.GetWorldPosition(mob.Owner));
        }
    }

    /// <summary>Whether nobody in <see cref="_spared"/> is in the blast or in the grenade's way.</summary>
    private bool SparesEveryone(Vector2 from, Vector2 spot, float radius)
    {
        foreach (var position in _spared)
        {
            if (Vector2.Distance(position, spot) <= radius || DistanceToSegment(position, from, spot) < 0.75f)
                return false;
        }

        return true;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var length = ab.LengthSquared();
        if (length <= 0f)
            return Vector2.Distance(point, a);
        var t = Math.Clamp(Vector2.Dot(point - a, ab) / length, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    /// <summary>A grenade in one of the crewman's weapon slots that is not already armed.</summary>
    private bool FindGrenade(EntityUid uid, [NotNullWhen(true)] out EntityUid? grenade, [NotNullWhen(true)] out string? slot)
    {
        grenade = null;
        slot = null;
        foreach (var name in WGCrewWeaponSystem.WeaponSlots)
        {
            if (!_inventory.TryGetSlotEntity(uid, name, out var item) || !HasComp<OnUseTimerTriggerComponent>(item)
                || HasComp<ActiveTimerTriggerComponent>(item))
                continue;
            grenade = item;
            slot = name;
            return true;
        }

        return false;
    }

    /// <summary>Whether he has a hand free for a grenade, or can free one by taking a two-handed gun in one.</summary>
    private bool HasThrowingHand(EntityUid uid, WGCrewWeaponComponent weapon)
    {
        if (_hands.TryGetEmptyHand(uid, out _))
            return true;
        return weapon.Drawn is { } gun && TryComp<WieldableComponent>(gun, out var wield) && wield.Wielded;
    }

    /// <summary>Takes the grenade in hand, shouts, arms it and keeps the hunt off the hostile until it has gone off.</summary>
    public bool Ready(EntityUid uid, EntityUid grenade, EntityUid target, EntityCoordinates at, WGCrewGrenadeRules rules)
    {
        if (!TryComp<WGCrewComponent>(uid, out var crew) || !TryComp<WGCrewWeaponComponent>(uid, out var weapon)
            || !TryComp<OnUseTimerTriggerComponent>(grenade, out var timer) || Transform(uid).GridUid is not { } grid
            || !FindGrenade(uid, out var carried, out var slot) || carried != grenade)
            return false;

        // A crewmate who planned in the same moment may have thrown first.
        var now = _timing.CurTime;
        if (_cooldowns.TryGetValue((grid, crew.Group), out var ready) && now < ready)
            return false;

        if (weapon.Drawn is { } gun)
            _weapons.Unwield(uid, gun);
        if (!TakeInHand(uid, grenade, slot))
        {
            Regrip(uid, weapon);
            return false;
        }

        var speed = CompOrNull<HandsComponent>(uid)?.BaseThrowspeed ?? 10f;
        var flight =Vector2.Distance(_xform.GetWorldPosition(uid), _xform.ToMapCoordinates(at).Position) / speed;
        var cook = Math.Clamp(WGCrewSkills.Of(crew).CookTime, 0f, MathF.Max(0f, timer.Delay - rules.MinFuse - flight));

        Shout(uid, grenade);
        _trigger.StartTimer((grenade, timer), uid);
        var armed = EnsureComp<WGCrewArmedGrenadeComponent>(uid);
        armed.Grenade = grenade;
        armed.At = at;
        armed.Target = target;
        armed.Rules = rules;
        armed.ThrowTime = now + TimeSpan.FromSeconds(cook);
        _cooldowns[(grid, crew.Group)] = now + TimeSpan.FromSeconds(rules.CrewCooldown);
        _weapons.HoldHunt(target, now + TimeSpan.FromSeconds(timer.Delay + 0.5f));
        return true;
    }

    /// <summary>Straight from the slot into a free hand, or unequipped first, as a weapon is drawn.</summary>
    private bool TakeInHand(EntityUid uid, EntityUid item, string slot)
    {
        if (_hands.TryPickupAnyHand(uid, item, checkActionBlocker: false, animateUser: false, animate: false))
            return true;
        if (!_inventory.TryUnequip(uid, slot, silent: true, force: true))
            return false;
        if (_hands.TryPickupAnyHand(uid, item, checkActionBlocker: false, animateUser: false, animate: false))
            return true;

        _inventory.TryEquip(uid, item, slot, silent: true, force: true);
        return false;
    }

    /// <summary>Says a grenade warning aloud. It is a fair warning, so it is never held back by speech cooldowns.</summary>
    private void Shout(EntityUid uid, EntityUid grenade)
    {
        var lines = HasComp<ExplodeOnTriggerComponent>(grenade) ? FragLines : OtherLines;
        _chat.TrySendInGameICMessage(uid, Loc.GetString(_random.Pick(lines)), InGameICChatType.Speak,
            hideChat: false, checkRadioPrefix: false);
    }

    /// <summary>Throws the held grenade once its cook time is up. True when there is nothing left to throw.</summary>
    public bool Tick(EntityUid uid)
    {
        if (!TryComp<WGCrewArmedGrenadeComponent>(uid, out var armed))
            return true;
        if (_timing.CurTime < armed.ThrowTime)
            return false;

        return TryThrowSafely((uid, armed));
    }

    /// <summary>
    /// Gets an armed grenade out of the crewman's hand as his task ends: thrown if it is safe to, else left to
    /// <see cref="Update"/> while the fuse allows; dropped if he can no longer throw.
    /// </summary>
    public void Release(EntityUid uid)
    {
        if (!TryComp<WGCrewArmedGrenadeComponent>(uid, out var armed))
            return;

        if (_mobs.IsAlive(uid) && _npcCombat.CanNpcShoot(uid))
            TryThrowSafely((uid, armed));
        else
            Drop((uid, armed));
    }

    /// <summary>
    /// Throws where it was aimed if that still spares everyone, or to a new spot that does. With no safe spot he
    /// keeps hold while the fuse allows, then throws as aimed: a blast in his hand is worse. True once thrown.
    /// </summary>
    private bool TryThrowSafely(Entity<WGCrewArmedGrenadeComponent> ent)
    {
        var (uid, armed) = ent;
        if (!TryComp<ActiveTimerTriggerComponent>(armed.Grenade, out var timer))
        {
            Throw(ent);
            return true;
        }

        var from = _xform.GetMapCoordinates(uid);
        var aimed = _xform.ToMapCoordinates(armed.At);
        var rules = armed.Rules;
        Spared(uid, armed.Target, from, rules);
        if (Vector2.Distance(from.Position, aimed.Position) > rules.BlastRadius
            && ClearThrow(uid, from, aimed)
            && SparesEveryone(from.Position, aimed.Position, rules.BlastRadius))
        {
            Throw(ent);
            return true;
        }

        if (TryFindLanding(uid, armed.Target, armed.Grenade, rules, out var landing, timer.TimeRemaining))
        {
            armed.At = landing;
            Throw(ent);
            return true;
        }

        var speed = CompOrNull<HandsComponent>(uid)?.BaseThrowspeed ?? 10f;
        var flight = Vector2.Distance(from.Position, aimed.Position) / speed;
        // Wait a tick for the way to clear, while a later throw still lands with its least fuse.
        if (timer.TimeRemaining - flight - (float) _timing.TickPeriod.TotalSeconds > rules.MinFuse)
            return false;

        Throw(ent);
        return true;
    }

    private void Throw(Entity<WGCrewArmedGrenadeComponent> ent)
    {
        var grenade = ent.Comp.Grenade;
        if (!TerminatingOrDeleted(grenade) && _hands.IsHolding(ent.Owner, grenade, out _)
            && _hands.TryDrop(ent.Owner, grenade, checkActionBlocker: false))
        {
            var direction = _xform.ToMapCoordinates(ent.Comp.At).Position - _xform.GetWorldPosition(ent.Owner);
            if (direction.LengthSquared() > 0.01f)
            {
                var speed = CompOrNull<HandsComponent>(ent.Owner)?.BaseThrowspeed ?? 10f;
                _throwing.TryThrow(grenade, direction, speed, ent.Owner, compensateFriction: true);
            }
        }

        Finish(ent);
    }

    private void Drop(Entity<WGCrewArmedGrenadeComponent> ent)
    {
        if (!TerminatingOrDeleted(ent.Comp.Grenade) && _hands.IsHolding(ent.Owner, ent.Comp.Grenade, out _))
            _hands.TryDrop(ent.Owner, ent.Comp.Grenade, checkActionBlocker: false);

        Finish(ent);
    }

    private void Finish(Entity<WGCrewArmedGrenadeComponent> ent)
    {
        RemComp<WGCrewArmedGrenadeComponent>(ent);
        if (TryComp<WGCrewWeaponComponent>(ent, out var weapon))
            Regrip(ent, weapon);
    }

    /// <summary>Takes the drawn gun back in hand, in both if it is two-handed.</summary>
    private void Regrip(EntityUid uid, WGCrewWeaponComponent weapon)
    {
        if (weapon.Drawn is not { } gun || !_hands.IsHolding(uid, gun, out _))
            return;
        _hands.TrySelect(uid, gun);
        _weapons.Wield(uid, gun);
    }

    /// <summary>
    /// Safety net: a crewman who goes down or stops thinking with an armed grenade in hand drops it, and one whose
    /// throw time passed without his task throwing it throws it.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGCrewArmedGrenadeComponent>();
        while (query.MoveNext(out var uid, out var armed))
        {
            if (TerminatingOrDeleted(armed.Grenade) || !_hands.IsHolding(uid, armed.Grenade, out _))
                Finish((uid, armed));
            else if (!_mobs.IsAlive(uid) || !_npcCombat.CanNpcShoot(uid))
                Drop((uid, armed));
            else if (now >= armed.ThrowTime + ThrowGrace)
                TryThrowSafely((uid, armed));
        }

        foreach (var (uid, until) in _nextTry)
        {
            if (now >= until)
                _nextTry.Remove(uid);
        }
    }
}
