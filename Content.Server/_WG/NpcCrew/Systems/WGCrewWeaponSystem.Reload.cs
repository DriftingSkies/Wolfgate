using Content.Server._WG.NpcCrew.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.Player;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Mobs.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.NPC;
using Content.Shared._WG.NpcCrew;
using System.Linq;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Reloads from real equipment slots and falls back to another weapon when ammunition is exhausted.</summary>
public sealed partial class WGCrewWeaponSystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private Content.Server.Atmos.EntitySystems.AtmosphereSystem _atmos = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private WGCrewEvaSystem _eva = default!;
    [Dependency] private WGCrewStealthSystem _stealth = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _xformSystem = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;

    private const string MagazineSlot = "gun_magazine";
    private const float LookupRange = 10f;
    private static readonly TimeSpan ThreatCacheTime = TimeSpan.FromSeconds(0.5);
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Threat)> _threats = new();

    /// <summary>Faction members found by one lookup, reused so a threat check allocates no set.</summary>
    private readonly HashSet<Entity<NpcFactionMemberComponent>> _nearbyMembers = new();

    /// <summary>Spare candidate lists; a target check may run another crewman's, so each check rents its own.</summary>
    private readonly Stack<List<EntityUid>> _candidateLists = new();
    private float _reloadTimer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _reloadTimer += frameTime;
        if (_reloadTimer >= 0.5f)
        {
            _reloadTimer = 0;
            foreach (var (mob, cached) in _threats)
            {
                if (_timing.CurTime >= cached.Until)
                    _threats.Remove(mob);
            }
            foreach (var (mob, cached) in _braces)
            {
                if (_timing.CurTime >= cached.Until)
                    _braces.Remove(mob);
            }
            PruneAdvance();
        }
        // Only awake crew are swept, each on its own jittered schedule.
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGCrewWeaponComponent, ActiveNPCComponent>();
        while (query.MoveNext(out var uid, out var weapon, out _))
        {
            if (now < weapon.NextCheck)
                continue;
            weapon.NextCheck = now + TimeSpan.FromSeconds(0.5 * (1 + 0.5 * _random.NextFloat()));
            if (HasComp<ActorComponent>(uid) || !_mobs.IsAlive(uid))
                continue;
            // Components made before the skill changed keep up; new ones start with it (OnRangedInit).
            if (TryComp<Content.Server.NPC.Components.NPCRangedCombatComponent>(uid, out var ranged) && TryComp<WGCrewComponent>(uid, out var shooter))
                ranged.ShootDelay = WGCrewSkills.Of(shooter).ShootDelay;
            if (TryComp<HTNComponent>(uid, out var plan)
                && plan.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager))
            {
                if (!CanEngage(uid, target))
                {
                    EntityManager.System<HTNSystem>().Replan(plan);
                    EntityManager.System<Content.Server.NPC.Systems.NPCSteeringSystem>().Unregister(uid);
                    plan.Blackboard.Remove<EntityUid>("Target");
                    RemCompDeferred<NPCRangedCombatComponent>(uid);
                    RemCompDeferred<NPCMeleeCombatComponent>(uid);
                }
                else
                    HoldAdvance(uid, target);
            }
            Track((uid, weapon), plan, now);
            // Mid-throw both hands are spoken for; the grenade system gives the gun back.
            if (HasComp<WGCrewArmedGrenadeComponent>(uid))
                continue;
            if (HasLiveThreat(uid) || IsBracing((uid, weapon)))
                TryReloadOrSwitch(uid);
            // A grabber's baton is out for someone who isn't a threat; holstering it here also restarts its cooldown for good.
            else if (!HasComp<WGCrewGrabberComponent>(uid))
            {
                RemCompDeferred<NPCRangedCombatComponent>(uid);
                RemCompDeferred<NPCMeleeCombatComponent>(uid);
                TryHolster(uid);
            }
        }
    }

    /// <summary>Only living hostile targets in the crewman's current vision justify drawing or reloading.</summary>
    /// <summary>Drops a crewman's cached threat check, so the next one looks afresh.</summary>
    public void ForgetThreat(EntityUid uid)
    {
        _threats.Remove(uid);
    }

    public bool HasLiveThreat(EntityUid uid)
    {
        var now = _timing.CurTime;
        if (_threats.TryGetValue(uid, out var cached) && now < cached.Until)
            return cached.Threat;
        var threat = PickTarget(uid) != null;
        // Spread over a second, so the crew's checks don't all fall due on the same tick.
        _threats[uid] = (now + ThreatCacheTime * (1 + _random.NextFloat()), threat);
        return threat;
    }

    /// <summary>
    /// Hostiles within normal vision: the set the faction lookup gives, without its allocations. The crew's explicit,
    /// shared and radio targets are walked by <see cref="PickTarget"/> itself.
    /// </summary>
    private void NearbyHostiles(EntityUid uid, NpcFactionMemberComponent member, List<EntityUid> candidates)
    {
        var range = TryComp<HTNComponent>(uid, out var htn)
            && htn.Blackboard.TryGetValue<float>("VisionRadius", out var vision, EntityManager) ? vision : LookupRange;
        // Alert-widened vision would turn the faction lookup into a scan of every faction member on the map.
        var hostileFactions = member.HostileFactions;
        _nearbyMembers.Clear();
        _lookup.GetEntitiesInRange(_xformSystem.GetMapCoordinates(uid), Math.Min(range, LookupRange), _nearbyMembers);
        foreach (var near in _nearbyMembers)
        {
            if (near.Owner != uid && hostileFactions.Overlaps(near.Comp.Factions)
                && !_factions.IsEntityFriendly((uid, member), (near.Owner, near.Comp)) && !_factions.IsIgnored(uid, near.Owner))
                candidates.Add(near.Owner);
        }
    }

    /// <summary>The faction lookup's hostility rule for one known mob.</summary>
    private bool IsHostile(EntityUid uid, NpcFactionMemberComponent? member, EntityUid target)
    {
        if (target == uid || TerminatingOrDeleted(target) || _factions.IsIgnored(uid, target))
            return false;
        if (_factions.GetHostiles(uid).Contains(target))
            return true;
        return member != null && _factions.IsMemberOfAny(target, member.HostileFactions)
            && !_factions.IsEntityFriendly(uid, target);
    }

    /// <summary>Crew defend their assigned ship without pursuing targets across docking connections.</summary>
    public bool CanEngage(EntityUid uid, EntityUid target) => Assess(uid, target) == WGCrewEngage.Engage;

    /// <summary>Whether the crewman fights a hostile, covers the approach to it unseen, or leaves it be.</summary>
    public WGCrewEngage Assess(EntityUid uid, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !_mobs.IsAlive(target) || Transform(uid).GridUid is not { } grid)
            return WGCrewEngage.No;

        // Someone it has finished with, or a boarding party's mark that has not hit it (NpcIntents).
        if (_factions.IsIgnored(uid, target) || EntityManager.System<Content.Server._WG.NpcIntents.WGNpcIntentSystem>().HoldsFireOn(uid, target))
            return WGCrewEngage.No;

        if (HasComp<WGCrewNonLethalComponent>(uid) && CompOrNull<Content.Shared.Cuffs.Components.CuffableComponent>(target)?.CuffedHandCount > 0)
            return WGCrewEngage.No;

        TryComp<WGCrewComponent>(uid, out var crew);
        if (OnSight(uid, crew, grid, target))
            return WGCrewEngage.Engage;
        if (Transform(target).GridUid != grid)
            return WGCrewEngage.No;

        var attacked = WasAttackedBy(uid, target);
        // Off his post's grid a crewman starts nothing. He answers whoever attacked him, in sight, only when sent
        // there on a job or a raid, or when the attacker is his faction's enemy anyway, as for a raider carried off;
        // one who only defends his ship goes home instead of following anyone off it.
        var away = crew?.Post is { } post && post.EntityId != grid;
        if (away && (!attacked || !OnErrand(uid, crew!, grid) && !IsFactionEnemy(uid, target)))
            return WGCrewEngage.No;
        if (IsStationCrew(uid) && !attacked)
            return WGCrewEngage.No;
        if (CanSee(uid, target))
            return WGCrewEngage.Engage;
        if (away)
            return WGCrewEngage.No;

        // Out of sight. Whoever was shot at goes after the shooter; of the rest the hunt party goes looking while the
        // others brace where they are. Nobody leaves good air to follow anyone into a spaced compartment, and nobody
        // walks into a grenade's blast.
        if (!attacked && !EntityManager.System<WGCrewCommsSystem>().Knows(uid, target))
            return WGCrewEngage.No;
        var hold = crew != null && MayBrace(uid, crew, target) ? WGCrewEngage.Brace : WGCrewEngage.No;
        if (crew is { Pursues: false } || HasAir(uid) && !HasAir(target) || BlastHeld(target))
            return hold;

        // Only a fresh attack sends him straight after someone he cannot see; an old grudge waits its turn in the hunt.
        return RecentlyAttackedBy(uid, target) || AdvanceFree(uid, target) ? WGCrewEngage.Engage : hold;
    }

    /// <summary>How long after an attack a crewman goes after an attacker he cannot see, outside the one-man hunt.</summary>
    private static readonly TimeSpan PursuitWindow = TimeSpan.FromSeconds(10);

    /// <summary>Whether the target struck this crewman himself within the pursuit window; a crewmate's attack does not count.</summary>
    private bool RecentlyAttackedBy(EntityUid uid, EntityUid target)
    {
        return TryComp<WGCrewComponent>(uid, out var crew) && crew.Struck.TryGetValue(target, out var at)
               && _timing.CurTime < at + PursuitWindow;
    }

    /// <summary>
    /// Whether an on-sight crewman opens fire on a faction enemy he sees: anywhere for the hands, only aboard for the
    /// helm, guns and radio, which stay manned against anyone outside.
    /// </summary>
    private bool OnSight(EntityUid uid, WGCrewComponent? crew, EntityUid grid, EntityUid target)
    {
        if (!TryComp<WGCrewOnSightComponent>(crew?.Post?.EntityId ?? grid, out var onSight)
            || Transform(target).GridUid != grid && IsStationCrew(uid)
            || !IsFactionEnemy(uid, target) || !CanSee(uid, target))
            return false;

        var company = WGCompanyFamily.Of(CompOrNull<Content.Shared._Mono.Company.CompanyComponent>(target)?.CompanyName.Id);
        return company.Length == 0 || !onSight.Spares.Any(spared => WGCompanyFamily.Of(spared) == company);
    }

    /// <summary>Whether the target belongs to a faction the crewman's own is hostile to, attack or no attack.</summary>
    private bool IsFactionEnemy(EntityUid uid, EntityUid target)
    {
        return TryComp<NpcFactionMemberComponent>(uid, out var member)
               && _factions.IsMemberOfAny(target, member.HostileFactions) && !_factions.IsEntityFriendly(uid, target);
    }

    /// <summary>Whether a crewman off his post's grid was sent aboard it: a work job, or his crew raiding it.</summary>
    private bool OnErrand(EntityUid uid, WGCrewComponent crew, EntityUid grid)
    {
        if (EntityManager.System<WGCrewWorkSystem>().HasJob(uid))
            return true;

        return crew.Post is { } post
               && EntityManager.System<WGCrewObjectiveSystem>().IsRaiding(post.EntityId, crew.Group, grid);
    }

    /// <summary>The longest one crewman keeps his place in the hunt for an unseen hostile without getting sight of it.</summary>
    private static readonly TimeSpan AdvanceLease = TimeSpan.FromSeconds(15);

    /// <summary>The crewmen hunting each hostile nobody can see, and how many the party admits.</summary>
    private readonly Dictionary<EntityUid, HuntParty> _advancing = new();

    /// <summary>Hostiles a grenade is about to go off beside: nobody goes after them until it has.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _blastHolds = new();

    private sealed class HuntParty
    {
        /// <summary>Set by whoever opens the hunt: his skill's <see cref="WGCrewSkills.Profile.HuntParty"/>.</summary>
        public int Size = 1;

        public readonly List<(EntityUid Crew, TimeSpan Until)> Members = new();
    }

    /// <summary>Whether a hunt member's lease still holds.</summary>
    private bool Live((EntityUid Crew, TimeSpan Until) member, TimeSpan now)
    {
        return now < member.Until && !TerminatingOrDeleted(member.Crew) && _mobs.IsAlive(member.Crew);
    }

    /// <summary>Whether the hunt for a hostile is open to this crewman: he is in it, or the party has room.</summary>
    private bool AdvanceFree(EntityUid uid, EntityUid target)
    {
        if (!_advancing.TryGetValue(target, out var party))
            return true;

        var now = _timing.CurTime;
        var live = 0;
        foreach (var member in party.Members)
        {
            if (member.Crew == uid)
                return true;
            if (Live(member, now))
                live++;
        }

        return live == 0 || live < party.Size;
    }

    /// <summary>Whether this crewman holds a live place in the hunt for a hostile.</summary>
    private bool InHunt(EntityUid uid, EntityUid target, TimeSpan now, out TimeSpan until)
    {
        until = default;
        if (!_advancing.TryGetValue(target, out var party))
            return false;

        foreach (var member in party.Members)
        {
            if (member.Crew != uid || !Live(member, now))
                continue;
            until = member.Until;
            return true;
        }

        return false;
    }

    /// <summary>Puts this crewman in the hunt until a time, opening it at his party size if nobody is in it.</summary>
    private void SetHunt(EntityUid uid, EntityUid target, TimeSpan until)
    {
        var now = _timing.CurTime;
        if (!_advancing.TryGetValue(target, out var party))
        {
            party = new HuntParty();
            _advancing[target] = party;
        }

        party.Members.RemoveAll(member => member.Crew == uid || !Live(member, now));
        if (party.Members.Count == 0)
            party.Size = TryComp<WGCrewComponent>(uid, out var crew) ? Math.Max(1, WGCrewSkills.Of(crew).HuntParty) : 1;
        party.Members.Add((uid, until));
    }

    /// <summary>
    /// Takes a place in the hunt for a hostile nobody can see. Only a plan that commits to the target calls this; an
    /// existing lease is never extended here, so a holder who gets nowhere lets go after the lease.
    /// </summary>
    public void ClaimAdvance(EntityUid uid, EntityUid target)
    {
        if (RecentlyAttackedBy(uid, target) || CanSee(uid, target) || !AdvanceFree(uid, target))
            return;

        var now = _timing.CurTime;
        if (InHunt(uid, target, now, out _))
            return;

        SetHunt(uid, target, now + AdvanceLease);
    }

    /// <summary>
    /// A crewman fighting a hostile in sight keeps his place in the hunt for it, so nobody else sets out meanwhile.
    /// One who cannot find a way to an unseen hostile gives his place up.
    /// </summary>
    private void HoldAdvance(EntityUid uid, EntityUid target)
    {
        if (!AdvanceFree(uid, target))
            return;

        var now = _timing.CurTime;
        var held = InHunt(uid, target, now, out var heldUntil);
        if (CanSee(uid, target))
        {
            var until = now + AdvanceLease;
            SetHunt(uid, target, held && heldUntil > until ? heldUntil : until);
        }
        else if (held && (Unreachable(uid)
                     || TryComp<NPCSteeringComponent>(uid, out var steering) && steering.Status == SteeringStatus.NoPath))
            SetHunt(uid, target, now);
    }

    /// <summary>Keeps everyone off a hostile until a grenade thrown at it has gone off.</summary>
    public void HoldHunt(EntityUid target, TimeSpan until)
    {
        if (!_blastHolds.TryGetValue(target, out var held) || held < until)
            _blastHolds[target] = until;
    }

    /// <summary>Whether a grenade is about to go off beside the hostile.</summary>
    public bool BlastHeld(EntityUid target)
    {
        return _blastHolds.TryGetValue(target, out var until) && _timing.CurTime < until;
    }

    private bool Unreachable(EntityUid uid)
    {
        return TryComp<NPCRangedCombatComponent>(uid, out var ranged) && ranged.Status == CombatStatus.TargetUnreachable
            || TryComp<NPCMeleeCombatComponent>(uid, out var melee) && melee.Status == CombatStatus.TargetUnreachable;
    }

    /// <summary>Drops leases that ran out and those whose crewman or hostile is gone, and spent blast holds.</summary>
    private void PruneAdvance()
    {
        var now = _timing.CurTime;
        foreach (var (target, party) in _advancing)
        {
            party.Members.RemoveAll(member => !Live(member, now));
            if (party.Members.Count == 0 || TerminatingOrDeleted(target) || !_mobs.IsAlive(target))
                _advancing.Remove(target);
        }

        foreach (var (target, until) in _blastHolds)
        {
            if (now >= until || TerminatingOrDeleted(target))
                _blastHolds.Remove(target);
        }
    }

    /// <summary>Whether a mob stands where there is air to breathe, or needs none.</summary>
    private bool HasAir(EntityUid mob)
    {
        if (_eva.IsImmune(mob))
            return true;
        var xform = Transform(mob);
        if (xform.GridUid is not { } grid || !TryComp<Robust.Shared.Map.Components.MapGridComponent>(grid, out var map))
            return false;

        var air = _atmos.GetTileMixture(grid, xform.MapUid, _maps.LocalToTile(grid, map, xform.Coordinates));
        return air != null && WGCrewPlannerSystem.IsBreathable(air, air.Pressure);
    }

    /// <summary>Station operators leave their job only to defend themselves against a personal attacker.</summary>
    public bool IsStationCrew(EntityUid uid) => TryComp<WGCrewComponent>(uid, out var crew)
        && (crew.Duty is WGCrewDuties.Pilot or WGCrewDuties.Gunnery
            || crew.Role == WGCrewRoles.Captain || crew.Role == WGCrewRoles.RadioOperator);

    private bool WasAttackedBy(EntityUid uid, EntityUid target) => TryComp<NPCRetaliationComponent>(uid, out var retaliation)
        && retaliation.AttackMemories.Any(memory => memory.Key == target && _timing.CurTime < memory.Value);

    /// <summary>
    /// Walls and closed opaque doors conceal boarders; radio awareness does not extend eyesight. Only what is built
    /// into the ship blocks the view: a crewmate, a body, a crate or a locker in between does not. A stealthed mob that
    /// has done nothing hostile lately is not seen, unless <paramref name="ignoreStealth"/>.
    /// </summary>
    public bool CanSee(EntityUid uid, EntityUid target, bool ignoreStealth = false) => !TerminatingOrDeleted(target)
        && (ignoreStealth || !_stealth.Unseen(target))
        && _interaction.InRangeUnobstructed(uid, target, 10f, Content.Shared.Physics.CollisionGroup.Opaque,
            predicate: blocker => !Transform(blocker).Anchored);

    /// <summary>Chooses a living hostile aboard the crewman's own ship.</summary>
    public EntityUid? PickTarget(EntityUid uid) => Pick(uid, WGCrewEngage.Engage);

    /// <summary>Chooses a hostile aboard whose approach the crewman covers because he may not go after it.</summary>
    public EntityUid? PickBraceTarget(EntityUid uid) => Pick(uid, WGCrewEngage.Brace);

    /// <summary>Chooses a hostile the crewman would fight that he can see right now.</summary>
    public EntityUid? PickVisibleTarget(EntityUid uid) => Pick(uid, WGCrewEngage.Engage, mustSee: true);

    /// <summary>The first known hostile the crewman would deal with as <paramref name="wanted"/>, and can see if asked.</summary>
    private EntityUid? Pick(EntityUid uid, WGCrewEngage wanted, bool mustSee = false)
    {
        bool Wanted(EntityUid target) => Assess(uid, target) == wanted && (!mustSee || CanSee(uid, target));

        TryComp<NpcFactionMemberComponent>(uid, out var member);
        if (member != null)
        {
            // The lookup's set is shared, so its hits are copied out before any check can run another lookup.
            var nearby = _candidateLists.TryPop(out var spare) ? spare : new List<EntityUid>();
            try
            {
                NearbyHostiles(uid, member, nearby);
                foreach (var target in nearby)
                {
                    if (Wanted(target))
                        return target;
                }
            }
            finally
            {
                nearby.Clear();
                _candidateLists.Push(nearby);
            }

            foreach (var target in _factions.GetHostiles(uid))
            {
                if (!_factions.IsIgnored(uid, target) && Wanted(target))
                    return target;
            }
        }

        if (!TryComp<WGCrewComponent>(uid, out var crew) || Transform(uid).GridUid is not { } grid)
            return null;
        foreach (var target in EntityManager.System<WGCrewAlertSystem>().GetSharedHostiles(grid, crew.Group))
        {
            if (IsHostile(uid, member, target) && Wanted(target))
                return target;
        }
        foreach (var target in crew.RadioSightings.Keys)
        {
            if (IsHostile(uid, member, target) && Wanted(target))
                return target;
        }
        return null;
    }

    /// <summary>Racks a loaded gun only when its chamber is empty or its bolt is open.</summary>
    private void ReadyChamber(EntityUid user, EntityUid gun)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(gun, out var chamber))
            return;
        if (chamber.BoltClosed == false)
            _guns.SetBoltClosed(gun, chamber, true, user);
        else if (_slots.TryGetSlot(gun, "gun_chamber", out var slot)
                 && (!slot.HasItem || slot.Item is { } round && TryComp<CartridgeAmmoComponent>(round, out var cartridge) && cartridge.Spent))
        {
            if (chamber.BoltClosed != null)
            {
                _guns.SetBoltClosed(gun, chamber, false, user);
                _guns.SetBoltClosed(gun, chamber, true, user);
                return;
            }
            RaiseLocalEvent(gun, new UseInHandEvent(user));
        }
    }

    /// <summary>Returns the ammunition reported by the item's ordinary provider.</summary>
    public int AmmoCount(EntityUid item)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(item, ref ev);
        if (HasComp<ChamberMagazineAmmoProviderComponent>(item)
            && _slots.TryGetSlot(item, "gun_chamber", out var chamber) && chamber.Item is { } round
            && TryComp<CartridgeAmmoComponent>(round, out var cartridge) && cartridge.Spent)
            return Math.Max(0, ev.Count - 1);
        return ev.Count;
    }

    /// <summary>Whether the crewman wearing this gun can reload it: a spare magazine, or never-dry ammo it can refill.</summary>
    private bool FindMagazineForOwner(EntityUid gun)
    {
        return Transform(gun).ParentUid is var owner && TryComp<WGCrewComponent>(owner, out var crew)
            && (crew.InfiniteAmmo && RefillHolder(gun) != null || FindMagazine(owner, gun, out _, out _));
    }

    private bool FindMagazine(EntityUid owner, EntityUid gun, out EntityUid magazine, out string equipmentSlot)
    {
        magazine = default;
        equipmentSlot = string.Empty;
        if (!_slots.TryGetSlot(gun, MagazineSlot, out var slot))
            return false;
        foreach (var name in WeaponSlots)
        {
            if (!_inventory.TryGetSlotEntity(owner, name, out var candidate) || candidate == gun
                || AmmoCount(candidate.Value) <= 0 || !_slots.CanInsert(gun, candidate.Value, owner, slot, swap: true))
                continue;
            magazine = candidate.Value;
            equipmentSlot = name;
            return true;
        }
        return false;
    }

    /// <summary>Reloads the held gun or holsters it and selects a usable fallback, leaving empty hands for melee.</summary>
    public bool TryReloadOrSwitch(EntityUid uid)
    {
        if (!TryComp<WGCrewWeaponComponent>(uid, out var weapon) || weapon.Drawn is not { } gun
            || !IsDrawn(uid) || !HasComp<GunComponent>(gun))
            return false;
        if (AmmoCount(gun) > 0)
        {
            ReadyChamber(uid, gun);
            return true;
        }

        if (TryComp<WGCrewComponent>(uid, out var crew) && crew.InfiniteAmmo && Refill(gun))
        {
            EntityManager.System<WGCrewSpeechSystem>().Say(uid, "reload");
            ReadyChamber(uid, gun);
            return AmmoCount(gun) > 0;
        }

        if (FindMagazine(uid, gun, out var magazine, out var equipmentSlot)
            && SwapMagazine(uid, gun, magazine, equipmentSlot))
        {
            EntityManager.System<WGCrewSpeechSystem>().Say(uid, "reload");
            ReadyChamber(uid, gun);
            return AmmoCount(gun) > 0;
        }

        Unwield(uid, gun);
        RemCompDeferred<NPCRangedCombatComponent>(uid);
        if (TryComp<HTNComponent>(uid, out var htn))
            EntityManager.System<HTNSystem>().Replan(htn);
        EntityManager.System<WGCrewSpeechSystem>().Say(uid, "empty");
        TryHolster(uid);
        TryDraw(uid);
        return false;
    }

    /// <summary>Fills the gun's own box or its loaded magazine back to capacity, for crew who never run dry.</summary>
    private bool Refill(EntityUid gun)
    {
        if (RefillHolder(gun) is not { } holder || !TryComp<BallisticAmmoProviderComponent>(holder, out var ammo))
            return false;

        var missing = ammo.Capacity - ammo.Container.ContainedEntities.Count;
        if (missing <= 0)
            return false;

        _guns.SetBallisticUnspawned((holder, ammo), missing);
        return true;
    }

    /// <summary>What <see cref="Refill"/> tops up: the gun's own ballistic store, else the magazine in it.</summary>
    private EntityUid? RefillHolder(EntityUid gun)
    {
        if (HasComp<BallisticAmmoProviderComponent>(gun))
            return gun;
        return _slots.TryGetSlot(gun, MagazineSlot, out var slot) && slot.Item is { } magazine
            && HasComp<BallisticAmmoProviderComponent>(magazine) ? magazine : null;
    }

    /// <summary>
    /// Loads a spare magazine straight from its equipment slot into the gun, without hands, so a wielded gun stays
    /// wielded. The spent magazine takes the spare's slot, or falls at his feet if it doesn't fit.
    /// </summary>
    private bool SwapMagazine(EntityUid uid, EntityUid gun, EntityUid magazine, string equipmentSlot)
    {
        if (!_slots.TryGetSlot(gun, MagazineSlot, out var slot)
            || !_inventory.TryUnequip(uid, equipmentSlot, silent: true, force: true))
            return false;

        EntityUid? spent = null;
        if (slot.HasItem && !_slots.TryEject(gun, slot, uid, out spent)
            || !_slots.TryInsert(gun, slot, magazine, uid))
        {
            if (spent != null)
                _slots.TryInsert(gun, slot, spent.Value, null);
            _inventory.TryEquip(uid, magazine, equipmentSlot, silent: true, force: true);
            return false;
        }

        if (spent != null)
            _inventory.TryEquip(uid, spent.Value, equipmentSlot, silent: true);
        return true;
    }
}
