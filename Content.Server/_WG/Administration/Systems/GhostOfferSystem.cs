using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Ghost.Roles.Raffles;
using Content.Shared._WG.Administration.GhostOffer;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Ghost.Roles.Raffles;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Administration.Systems;

/// <summary>
/// Admin "offer to ghosts": makes an entity a raffled ghost role if it isn't one, then pops a sign-up prompt for
/// every current ghost until the role is taken or gone.
/// </summary>
public sealed partial class GhostOfferSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private GhostRoleSystem _ghostRoles = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>The draw ERT sign-ups use: 10 s from the first sign-up, 15 s at most.</summary>
    private static readonly ProtoId<GhostRoleRaffleSettingsPrototype> RaffleSettings = "short";

    private static readonly SoundSpecifier OfferSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    /// <summary>Seconds between checks for offers whose role was taken or is gone.</summary>
    private const float CheckInterval = 1f;

    /// <summary>
    /// Open offers by id, each the entity holding the ghost role.
    /// </summary>
    private readonly Dictionary<int, EntityUid> _offers = new();

    /// <summary>Not reset between rounds, so a stale prompt can't match a new offer.</summary>
    private int _lastOfferId;

    private float _checkAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GhostOfferSignUpEvent>(OnSignUp);
        SubscribeNetworkEvent<GhostOfferFollowEvent>(OnFollow);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_offers.Count == 0)
            return;

        _checkAccumulator += frameTime;
        if (_checkAccumulator < CheckInterval)
            return;

        _checkAccumulator = 0;
        foreach (var (id, uid) in _offers.ToList())
        {
            if (GetOpenRole(uid) == null)
                Close(id);
        }
    }

    /// <summary>
    /// Whether the verb should show: a mob no player is in, or an entity whose ghost role is still open.
    /// </summary>
    public bool CanOffer(EntityUid uid)
    {
        if (HasComp<GhostRoleComponent>(uid))
            return GetOpenRole(uid) != null;

        return HasComp<MobStateComponent>(uid) && !HasPlayer(uid);
    }

    /// <summary>
    /// Offers <paramref name="uid"/> to every ghost. An entity that isn't a ghost role yet becomes a raffled one named
    /// after it. Blank texts keep the role's current ones.
    /// </summary>
    public bool TryOffer(EntityUid uid, ICommonSession? admin, string? name, string? description, string? rules,
        out string message)
    {
        if (TerminatingOrDeleted(uid))
        {
            message = Loc.GetString("wg-ghost-offer-error-deleted");
            return false;
        }

        Entity<GhostRoleComponent> role;
        if (HasComp<GhostRoleComponent>(uid))
        {
            if (GetOpenRole(uid) is not { } open)
            {
                message = Loc.GetString("wg-ghost-offer-error-unavailable", ("entity", ToPrettyString(uid)));
                return false;
            }

            role = open;
        }
        else
        {
            if (HasPlayer(uid))
            {
                message = Loc.GetString("wg-ghost-offer-error-has-player", ("entity", ToPrettyString(uid)));
                return false;
            }

            // Same setup as makeghostroleraffled; the raffle itself only starts with the first sign-up.
            var added = AddComp<GhostRoleComponent>(uid);
            EnsureComp<GhostTakeoverAvailableComponent>(uid);
            added.RaffleConfig = new GhostRoleRaffleConfig(_prototypeManager.Index(RaffleSettings).Settings);
            added.RoleName = Name(uid);
            added.RoleDescription = Loc.GetString("wg-ghost-offer-default-description", ("name", Name(uid)));
            added.RoleRules = Loc.GetString("ghost-role-component-default-rules");
            role = (uid, added);
        }

        if (!string.IsNullOrWhiteSpace(name))
            role.Comp.RoleName = name.Trim();
        if (!string.IsNullOrWhiteSpace(description))
            role.Comp.RoleDescription = description.Trim();
        if (!string.IsNullOrWhiteSpace(rules))
            role.Comp.RoleRules = rules.Trim();

        // Offering it again re-sends the prompt, reaching ghosts who closed it and players who ghosted since.
        var id = _offers.FirstOrDefault(pair => pair.Value == uid).Key;
        if (id == 0)
        {
            id = ++_lastOfferId;
            _offers[id] = uid;
        }

        var ghosts = Filter.Empty().AddWhere(session =>
            session.AttachedEntity is { } entity && HasComp<GhostComponent>(entity));
        var offer = new GhostOfferEvent(id, role.Comp.RoleName, role.Comp.RoleDescription, role.Comp.RoleRules,
            role.Comp.RaffleConfig != null);

        RaiseNetworkEvent(offer, ghosts);
        _audio.PlayGlobal(OfferSound, ghosts, true);

        var count = ghosts.Recipients.Count();
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{admin?.Name ?? "Server"} offered {ToPrettyString(uid):entity} to {count} ghosts as \"{role.Comp.RoleName}\"");

        message = Loc.GetString("wg-ghost-offer-sent", ("name", role.Comp.RoleName), ("count", count));
        return true;
    }

    private void OnSignUp(GhostOfferSignUpEvent ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        var isError = true;
        string message;

        if (!IsGhost(player))
        {
            message = Loc.GetString("wg-ghost-offer-signup-not-ghost");
        }
        else if (!_offers.TryGetValue(ev.OfferId, out var uid) || GetOpenRole(uid) is not { } role)
        {
            message = Loc.GetString("wg-ghost-offer-signup-closed");
        }
        else if (role.Comp.RaffleConfig == null)
        {
            // First come, first served. The next check closes the offer once the role is used up.
            if (_ghostRoles.Takeover(player, role.Comp.Identifier))
            {
                message = Loc.GetString("wg-ghost-offer-signup-taken", ("name", role.Comp.RoleName));
                isError = false;
            }
            else
            {
                message = Loc.GetString("wg-ghost-offer-signup-failed", ("name", role.Comp.RoleName));
            }
        }
        else if (InRaffle(role, player))
        {
            message = Loc.GetString("wg-ghost-offer-signup-already");
        }
        else
        {
            _ghostRoles.Request(player, role.Comp.Identifier);

            // Request is silent when it refuses, such as a role whitelist the player isn't on.
            isError = !InRaffle(role, player);
            message = Loc.GetString(isError ? "wg-ghost-offer-signup-failed" : "wg-ghost-offer-signup-joined",
                ("name", role.Comp.RoleName));
        }

        RaiseNetworkEvent(new GhostOfferSignUpResultEvent(ev.OfferId, message, isError), Filter.SinglePlayer(player));
    }

    private void OnFollow(GhostOfferFollowEvent ev, EntitySessionEventArgs args)
    {
        // Following moves the follower to the target, so only ghosts may.
        if (!IsGhost(args.SenderSession)
            || !_offers.TryGetValue(ev.OfferId, out var uid)
            || GetOpenRole(uid) is not { } role)
            return;

        _ghostRoles.Follow(args.SenderSession, role.Comp.Identifier);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var id in _offers.Keys)
        {
            RaiseNetworkEvent(new GhostOfferClosedEvent(id));
        }

        _offers.Clear();
    }

    private void Close(int id)
    {
        _offers.Remove(id);
        RaiseNetworkEvent(new GhostOfferClosedEvent(id));
    }

    /// <summary>
    /// The entity's ghost role while it can still be signed up for: not taken and listed in the ghost roles menu.
    /// </summary>
    private Entity<GhostRoleComponent>? GetOpenRole(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid)
            || !TryComp<GhostRoleComponent>(uid, out var role)
            || role.Taken
            || !_ghostRoles.GhostRoles.Any(listed => listed.Owner == uid))
            return null;

        return (uid, role);
    }

    private bool InRaffle(EntityUid uid, ICommonSession player)
    {
        if (!TryComp<GhostRoleRaffleComponent>(uid, out var raffle))
            return false;

        // Through a local: the component's access rules allow reading the member but not calling it.
        var entrants = raffle.CurrentMembers;
        return entrants.Contains(player);
    }

    /// <summary>
    /// A player is in it, or its mind is (a disconnected or visiting player).
    /// </summary>
    private bool HasPlayer(EntityUid uid)
    {
        return HasComp<ActorComponent>(uid) || TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind;
    }

    private bool IsGhost(ICommonSession player)
    {
        return player.AttachedEntity is { } entity && HasComp<GhostComponent>(entity);
    }
}
