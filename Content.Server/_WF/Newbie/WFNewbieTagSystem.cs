using Content.Server.Players.PlayTimeTracking;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Newbie;
using Content.Shared.Ghost;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Shuttles.Components;
using Content.Shared.Traits;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Newbie;

/// <summary>
/// Tags the bodies and ships of players under the newbie playtime, unless they turned their tag off.
/// </summary>
public sealed partial class WFNewbieTagSystem : EntitySystem
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private INetConfigurationManager _netCfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _playTime = default!;

    private TimeSpan _nextRefresh;
    private readonly Dictionary<EntityUid, bool> _ships = new();
    private readonly List<EntityUid> _untag = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        RefreshAll();
    }

    /// <summary>
    /// Brings every attached body in line with its player's tag, and tags each ship a new player owns or is flying.
    /// </summary>
    public void RefreshAll()
    {
        // Ship to whether its owner is new; a ship only a new pilot flies maps to false.
        _ships.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } body)
                continue;

            var tagged = IsTagged(session);
            SetTag(body, tagged);
            if (tagged && TryComp<PilotComponent>(body, out var pilot) && pilot.Console is { } console
                && Transform(console).GridUid is { } piloted)
                _ships.TryAdd(piloted, false);
        }

        var owned = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (owned.MoveNext(out var ship, out var owner))
        {
            // An offline owner, or one whose playtime hasn't loaded, keeps whatever owner tag their ship last had.
            if (!_players.TryGetSessionById(owner.OwnerUserId, out var session) || !TryGetTagged(session, out var ownerTagged))
                ownerTagged = CompOrNull<WFNewbieShipComponent>(ship)?.OwnerTagged ?? false;
            if (ownerTagged)
                _ships[ship] = true;
        }

        _untag.Clear();
        var shown = EntityQueryEnumerator<WFNewbieShipComponent>();
        while (shown.MoveNext(out var ship, out _))
        {
            if (!_ships.ContainsKey(ship))
                _untag.Add(ship);
        }

        foreach (var ship in _untag)
        {
            RemComp<WFNewbieShipComponent>(ship);
        }

        foreach (var (ship, ownerTagged) in _ships)
        {
            EnsureComp<WFNewbieShipComponent>(ship).OwnerTagged = ownerTagged;
        }
    }

    /// <summary>
    /// Whether a player is under the newbie playtime and shows their tag.
    /// </summary>
    public bool IsTagged(ICommonSession session)
    {
        // Playtime loads from the database after connecting; until then nobody is tagged.
        return TryGetTagged(session, out var tagged) && tagged;
    }

    /// <summary>
    /// Whether a player is tagged, or false if that isn't known yet because their playtime hasn't loaded.
    /// </summary>
    public bool TryGetTagged(ICommonSession session, out bool tagged)
    {
        tagged = false;
        var hours = _cfg.GetCVar(WFNewbieCVars.Hours);
        if (hours <= 0 || session.Status != SessionStatus.InGame)
            return true;

        if (!TryGetPlaytime(session, out var playtime))
            return false;

        tagged = playtime < TimeSpan.FromHours(hours) && _netCfg.GetClientCVar(session.Channel, WFNewbieCVars.ShowTag);
        return true;
    }

    /// <summary>
    /// Whether a tagged player is barred from a trait marked <see cref="TraitPrototype.NewbieBlocked"/>.
    /// </summary>
    public bool TraitBlocked(TraitPrototype trait, ICommonSession session)
    {
        return trait.NewbieBlocked && IsTagged(session);
    }

    /// <summary>
    /// A player's total playtime, once it has loaded from the database.
    /// </summary>
    public bool TryGetPlaytime(ICommonSession session, out TimeSpan playtime)
    {
        playtime = TimeSpan.Zero;
        if (!_playTime.TryGetTrackerTimes(session, out var times))
            return false;

        // Stored times lag behind the session until flushed, which would keep a player tagged past the threshold.
        _playTime.FlushTracker(session);
        playtime = times.GetValueOrDefault(PlayTimeTrackingShared.TrackerOverall);
        return true;
    }

    private void OnAttached(PlayerAttachedEvent args)
    {
        SetTag(args.Entity, IsTagged(args.Player));
    }

    private void OnDetached(PlayerDetachedEvent args)
    {
        SetTag(args.Entity, false);
    }

    private void SetTag(EntityUid body, bool tagged)
    {
        // Ghosts are only seen by other ghosts, who have no use for the mark.
        if (tagged && !HasComp<GhostComponent>(body))
            EnsureComp<WFNewbieComponent>(body);
        else
            RemComp<WFNewbieComponent>(body);
    }
}
