using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipShields;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Stack;
using Content.Shared._Mono.Company;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Rewards players who take a side in an encounter. Whose side you are on is decided by who you shoot: ship
/// weapon hits on one side's ships count as help for the others. A ship that has helped a side and not fired on
/// it becomes that side's ally for the encounter. When one side is the only one left, everyone who helped it
/// shares its reward equally, paid into their bank accounts, with faction credits on top for its own company.
/// </summary>
public sealed partial class WFEncounterRewardSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private Content.Server.Shuttles.Systems.DockingSystem _docking = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Hits on a side's ships before a player ship counts as fighting it.</summary>
    public const int MinimumHits = 5;

    /// <summary>How near a stranded ship a player must be when it gets under way to share its rescue reward.</summary>
    public const float RescueRange = 150f;

    private static readonly ProtoId<RadioChannelPrototype> ThanksChannel = "Common";
    private static readonly TimeSpan CapWindow = TimeSpan.FromHours(1);
    private readonly Dictionary<NetUserId, (TimeSpan Since, int Paid)> _paid = new();

    public override void Initialize()
    {
        base.Initialize();
        // After the crews have judged the hit: a stray shot from a ship's own side is no help to its enemies.
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit, after: [typeof(WFCrewAlertSystem)]);
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnShieldHit, after: [typeof(WFCrewAlertSystem)]);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WFCrewFriendlyFireQueryEvent>(OnFriendlyFireQuery);
    }

    private void OnHullHit(ref WFCrewHullHitEvent args)
    {
        // Judged before it is counted, as the crews judged it, so the same hit is never weighed twice.
        RecordHit(args.Grid, args.AttackerGrid);
        Track(args.Grid, args.AttackerGrid);
    }

    private void OnShieldHit(ref WFShipShieldAttackedEvent args)
    {
        // A handheld shot at a shield is no ship weapon hit.
        if (!_alerts.IsShipWeapon(args.Weapon))
            return;

        RecordHit(args.Grid, args.AttackerGrid);
        Track(args.Grid, args.AttackerGrid);
    }

    /// <summary>How long a vessel's hits count toward whom it is fighting.</summary>
    private static readonly TimeSpan FireWindow = TimeSpan.FromSeconds(30);

    /// <summary>How many times over its hits on a side a vessel must have hit that side's enemies to be its friend.</summary>
    private const int FriendlyFireRatio = 2;

    /// <summary>Counts a ship weapon hit on an encounter ship by any vessel outside the encounter, excused or not.</summary>
    private void Track(EntityUid victim, EntityUid attacker)
    {
        if (!TryComp<WFEncounterGridComponent>(victim, out var marker) || HasComp<WFEncounterGridComponent>(attacker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var encounter)
            || !encounter.Ships.TryGetValue(marker.Key, out var ship))
            return;

        var now = _timing.CurTime;
        var key = (attacker, ship.Side);
        if (!encounter.Fire.TryGetValue(key, out var fire))
            encounter.Fire[key] = fire = new List<TimeSpan>();
        fire.RemoveAll(hit => now - hit > FireWindow);
        fire.Add(now);
    }

    /// <summary>
    /// Vouches for a vessel to a ship's crew: it is their friend while, over the last half minute, it has hit their
    /// side's enemies at least twice as often as it has hit them, this hit included. In a melee the odd round goes
    /// astray; a vessel plainly fighting their fight is not an enemy for it. Conduct alone decides, an alliance
    /// notwithstanding: an ally that turns its guns on the side loses the excuse, and with it the alliance.
    /// </summary>
    private void OnFriendlyFireQuery(ref WFCrewFriendlyFireQueryEvent args)
    {
        if (args.Friendly || !TryComp<WFEncounterGridComponent>(args.Grid, out var marker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var encounter)
            || !encounter.Ships.TryGetValue(marker.Key, out var ship))
            return;

        var now = _timing.CurTime;
        var onUs = 0;
        var onEnemies = 0;
        foreach (var ((attacker, side), fire) in encounter.Fire)
        {
            if (attacker != args.Attacker)
                continue;
            var recent = fire.Count(hit => now - hit <= FireWindow);
            if (side == ship.Side)
                onUs += recent;
            else
                onEnemies += recent;
        }

        args.Friendly = onEnemies >= FriendlyFireRatio * (onUs + 1);
    }

    /// <summary>Counts a ship weapon hit by a player-crewed ship on an encounter ship as help for the other sides.</summary>
    public void RecordHit(EntityUid victim, EntityUid attacker)
    {
        if (_alerts.IsExcused(victim, attacker))
            return;

        if (!TryComp<WFEncounterGridComponent>(victim, out var marker) || HasComp<WFEncounterGridComponent>(attacker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var encounter) || encounter.Resolution != null
            || !encounter.Ships.TryGetValue(marker.Key, out var ship))
            return;

        var crew = new List<NetUserId>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.GridUid == attacker && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                crew.Add(actor.PlayerSession.UserId);
        }

        if (crew.Count == 0)
            return;

        foreach (var player in crew)
        {
            if (!encounter.Hits.TryGetValue(player, out var bySide))
                encounter.Hits[player] = bySide = new Dictionary<string, int>();
            bySide[ship.Side] = bySide.GetValueOrDefault(ship.Side) + 1;
        }

        var hits = encounter.ShipHits.GetValueOrDefault((attacker, ship.Side)) + 1;
        encounter.ShipHits[(attacker, ship.Side)] = hits;
        // Firing on a side that took the ship for an ally ends the alliance.
        if (hits == 1)
            RevokeAlliance(encounter, attacker, ship.Side);
        if (hits != MinimumHits)
            return;

        // Having fought this side and hardly the others, the ship is on their side: they hold their fire for it.
        foreach (var other in encounter.Ships.Values)
        {
            if (other.Side == ship.Side || TerminatingOrDeleted(other.Grid)
                || encounter.ShipHits.GetValueOrDefault((attacker, other.Side)) * FriendlyFireRatio > hits)
                continue;

            _escorts.AddAlly(other.Grid, attacker);
            encounter.Allies.Add((other.Grid, attacker));
        }
    }

    /// <summary>Ends the alliances the ships of a side made with a player ship that has now fired on them.</summary>
    private void RevokeAlliance(WFEncounterComponent encounter, EntityUid attacker, string side)
    {
        for (var i = encounter.Allies.Count - 1; i >= 0; i--)
        {
            var (held, ally) = encounter.Allies[i];
            if (ally != attacker || !encounter.Ships.Values.Any(other => other.Grid == held && other.Side == side))
                continue;

            _escorts.RemoveAlly(held, ally);
            encounter.Allies.RemoveAt(i);
        }
    }

    /// <summary>
    /// The one side with a ship still in the fight, or null when none or several are. Judged as the encounter was:
    /// by who is fit to fight with living crew, so a side whose crews are dead has lost whatever state its hulls are in.
    /// </summary>
    public string? DecidedSide(WFEncounterComponent encounter)
    {
        string? side = null;
        foreach (var ship in encounter.Ships.Values)
        {
            if (!InFight(ship))
                continue;

            if (side != null && side != ship.Side)
                return null;

            side = ship.Side;
        }

        return side;
    }

    /// <summary>A ship is in the fight while it is fit to fight and any of its crew live, aboard or not.</summary>
    private bool InFight(WFEncounterShipState ship)
    {
        return _encounters.InFight(ship);
    }

    private void OnResolved(ref WFEncounterResolvedEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter))
            return;

        foreach (var (ship, ally) in encounter.Allies)
        {
            _escorts.RemoveAlly(ship, ally);
        }

        encounter.Allies.Clear();
        if (args.Resolution != WFEncounterResolution.Decided || !_prototypes.TryIndex(encounter.Prototype, out var prototype))
            return;

        // The winner is the one side the encounter was decided for.
        if (DecidedSide(encounter) is not { } side
            || prototype.Rewards.FirstOrDefault(entry => entry.Side == side) is not { } reward)
            return;

        // A destroyed hull is gone by now, so its company is read from the prototype as well.
        var losers = new HashSet<string>();
        foreach (var (key, ship) in encounter.Ships)
        {
            if (ship.Side == side)
                continue;

            if (prototype.Ships.FirstOrDefault(entry => entry.Key == key)?.Company?.Id is { Length: > 0 } listed)
                losers.Add(listed);
            if (!TerminatingOrDeleted(ship.Grid)
                && CompOrNull<CompanyComponent>(ship.Grid)?.CompanyName.Id is { Length: > 0 } company)
                losers.Add(company);
        }

        var helpers = new List<(ICommonSession Session, EntityUid Mob)>();
        foreach (var (user, bySide) in encounter.Hits)
        {
            var against = bySide.Where(pair => pair.Key != side).Sum(pair => pair.Value);
            if (against < MinimumHits || bySide.GetValueOrDefault(side) > 0
                || !_playerManager.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } mob
                || HasComp<GhostComponent>(mob) || _mobs.IsDead(mob))
                continue;

            // Nobody is paid for helping to destroy a ship of their own company.
            if (CompOrNull<CompanyComponent>(mob)?.CompanyName.Id is { Length: > 0 } own && losers.Contains(own))
                continue;

            helpers.Add((session, mob));
        }

        if (helpers.Count == 0)
            return;

        Pay(args.Encounter, encounter, reward, helpers, $"helping side {side} win");

        // Whichever ship of the winning side can still speak says the thanks.
        if (reward.Thanks is { } thanks)
        {
            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.Side == side && _encounters.TrySay(ship, ThanksChannel, Loc.GetString(thanks, ("count", helpers.Count))))
                    break;
            }
        }
    }

    /// <summary>
    /// Pays a rescued ship's side reward among the living players aboard it, aboard a ship docked with it, or within
    /// <see cref="RescueRange"/> of it, and the ship says its thanks.
    /// </summary>
    public void PayRescue(Entity<WFEncounterComponent> encounter, WFEncounterShipState ship)
    {
        if (TerminatingOrDeleted(ship.Grid) || !_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype)
            || prototype.Rewards.FirstOrDefault(entry => entry.Side == ship.Side) is not { } reward)
            return;

        var docked = new HashSet<EntityUid>();
        foreach (var dock in _docking.GetDocks(ship.Grid))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } grid)
                docked.Add(grid);
        }

        var here = _transform.GetMapCoordinates(ship.Grid);
        var helpers = new List<(ICommonSession Session, EntityUid Mob)>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (HasComp<GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            var aboard = xform.GridUid is { } grid && (grid == ship.Grid || docked.Contains(grid));
            if (!aboard && (xform.MapID != here.MapId
                    || (_transform.GetWorldPosition(xform) - here.Position).LengthSquared() > RescueRange * RescueRange))
                continue;

            helpers.Add((actor.PlayerSession, uid));
        }

        if (helpers.Count == 0)
            return;

        Pay(encounter, encounter.Comp, reward, helpers, $"rescuing a ship of side {ship.Side} in");
        if (reward.Thanks is { } thanks)
            _encounters.TrySay(ship, ThanksChannel, Loc.GetString(thanks, ("count", helpers.Count)));
    }

    /// <summary>
    /// Shares a reward's spesos equally among the helpers within the hourly cap, hands faction credits to those of its
    /// companies, and tells each what they got.
    /// </summary>
    private void Pay(EntityUid uid, WFEncounterComponent encounter, WFEncounterReward reward,
        List<(ICommonSession Session, EntityUid Mob)> helpers, string deed)
    {
        var share = reward.Spesos / helpers.Count;
        var cap = _config.GetCVar(EncountersCVars.PayoutHourlyCap);
        foreach (var (session, mob) in helpers)
        {
            var paid = Capped(session.UserId, share, cap);
            // A deposit that fails, with no bank account or deposits off, pays nothing and keeps the allowance.
            if (paid > 0 && !_bank.TryBankDeposit(mob, paid, false))
            {
                Refund(session.UserId, paid);
                paid = 0;
            }

            var credits = 0;
            if (reward.Credits > 0 && reward.CreditEntity is { } creditEntity
                && CompOrNull<CompanyComponent>(mob)?.CompanyName.Id is { } company && reward.CreditCompanies.Contains(company))
            {
                credits = reward.Credits;
                foreach (var stack in _stack.SpawnMultiple(creditEntity, credits, Transform(mob).Coordinates))
                {
                    _hands.PickupOrDrop(mob, stack);
                }
            }

            if (paid == 0 && credits == 0)
                continue;

            _chat.DispatchServerMessage(session, Loc.GetString(credits > 0 ? "wf-encounter-reward-paid-credits" : "wf-encounter-reward-paid",
                ("name", encounter.Name), ("amount", paid), ("credits", credits)));
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{session.Name} was paid {paid} spesos and {credits} faction credits for {deed} encounter {encounter.Prototype} ({ToPrettyString(uid):entity})");
        }
    }

    /// <summary>What of an amount a player may still be paid within the hourly cap, and counts it. The window opens at the first payout.</summary>
    private int Capped(NetUserId user, int amount, int cap)
    {
        var now = _timing.CurTime;
        var (since, paid) = _paid.TryGetValue(user, out var entry) ? entry : (now, 0);
        if (now - since >= CapWindow)
        {
            since = now;
            paid = 0;
        }

        var allowed = Math.Clamp(cap - paid, 0, amount);
        _paid[user] = (since, paid + allowed);
        return allowed;
    }

    /// <summary>Gives back an allowance <see cref="Capped"/> counted for a payment that did not go through.</summary>
    private void Refund(NetUserId user, int amount)
    {
        if (_paid.TryGetValue(user, out var entry))
            _paid[user] = (entry.Since, Math.Max(0, entry.Paid - amount));
    }
}
