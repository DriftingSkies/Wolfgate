using Content.Server._WG.Defcon;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Wolfmed.Life;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Server.Roles;
using Content.Shared._WG.Wolfmed.Life;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Roles;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Random;
using Content.Shared._WG.Lattice;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Lattice;

/// <summary>
/// The Choir, as players. Someone whose Drift runs very high hears the song once a round and gets an action to answer
/// it; nobody joins without using it. A Choir player takes the Choir's faction, stops hallucinating, is briefed on
/// what the Choir wants, and counts towards DEFCON. Admins are told of each offer and each join. Population and member
/// caps keep it out of quiet rounds.
/// </summary>
public sealed partial class WGChoirSystem : EntitySystem
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private WGDeepRisingSystem _rising = default!;
    [Dependency] private WGRemadeConversionSystem _conversion = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public const string Faction = "WGChoir";
    private const string ActionPrototype = "WGChoirActionAnswer";
    private const string MindRole = "WGMindRoleChoir";

    /// <summary>Chance that answering the song turns the player Remade instead.</summary>
    private const float AbominationChance = 0.5f;

    /// <summary>How long a dead Choir member lies before the body turns.</summary>
    private static readonly TimeSpan TurnAfter = TimeSpan.FromSeconds(30);

    /// <summary>How long the offer stays open.</summary>
    private static readonly TimeSpan OfferTime = TimeSpan.FromMinutes(5);

    private readonly HashSet<NetUserId> _offered = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _offered.Clear());
        SubscribeLocalEvent<WGChoirOfferComponent, WGChoirAnswerEvent>(OnAnswer);
        SubscribeLocalEvent<WGChoirOfferComponent, ComponentShutdown>(OnOfferShutdown);
        SubscribeLocalEvent<WGChoirMemberComponent, ComponentShutdown>(OnMemberShutdown);
        SubscribeLocalEvent<WGDefconGatherEvent>(OnDefconGather);
        SubscribeLocalEvent<WGChoirMarkComponent, ExaminedEvent>(OnMarkExamined);
    }

    /// <summary>Offers the song to a player deep enough in Drift, if the round allows it and they haven't had it.</summary>
    public void Consider(ICommonSession session, EntityUid mob, float drift)
    {
        if (drift < _config.GetCVar(WGChoirCVars.MinDrift)
            || !_config.GetCVar(WGChoirCVars.Enabled)
            || _mobState.IsDead(mob)
            || HasComp<WGChoirMemberComponent>(mob)
            || HasComp<WGChoirOfferComponent>(mob)
            || _offered.Contains(session.UserId)
            || _player.PlayerCount < _config.GetCVar(WGChoirCVars.MinPlayers)
            || Members() >= _config.GetCVar(WGChoirCVars.MaxMembers))
            return;

        Offer(session, mob);
    }

    /// <summary>Gives a player the offer and its action, whatever the caps say.</summary>
    public void Offer(ICommonSession session, EntityUid mob)
    {
        _offered.Add(session.UserId);
        var offer = EnsureComp<WGChoirOfferComponent>(mob);
        offer.Expires = _timing.CurTime + OfferTime;
        offer.Action = _actions.AddAction(mob, ActionPrototype);
        _popup.PopupEntity(Loc.GetString("wg-choir-offer-popup"), mob, session, PopupType.LargeCaution);
        Tell(session, Loc.GetString("wg-choir-offer"));
        _chat.SendAdminAlert(mob, Loc.GetString("wg-choir-admin-offered"));
    }

    /// <summary>Makes someone Choir.</summary>
    public void Join(EntityUid mob)
    {
        RemComp<WGChoirOfferComponent>(mob);
        if (HasComp<WGChoirMemberComponent>(mob))
            return;

        EnsureComp<WGChoirMemberComponent>(mob);
        _factions.AddFaction((mob, null), Faction);
        var unrevivable = EnsureComp<UnrevivableComponent>(mob);
        unrevivable.ReasonMessage = "wg-choir-unrevivable";
        Dirty(mob, unrevivable);
        if (_mind.TryGetMind(mob, out var mindId, out var mind) && !_roles.MindHasRole<WGChoirRoleComponent>(mindId))
            _roles.MindAddRole(mindId, MindRole, mind, true);

        MarkPda(mob);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(mob):player} joined the Choir");
        _chat.SendAdminAlert(mob, Loc.GetString("wg-choir-admin-joined"));
        if (_player.TryGetSessionByEntity(mob, out var session))
        {
            _popup.PopupEntity(Loc.GetString("wg-choir-joined-popup"), mob, session, PopupType.LargeCaution);
            Tell(session, Loc.GetString("wg-choir-briefing"));
        }
    }

    /// <summary>How many living Choir members a player still controls; dead bodies and abandoned ones don't count.</summary>
    public int Members()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WGChoirMemberComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_mobState.IsDead(uid) && _player.TryGetSessionByEntity(uid, out _))
                count++;
        }

        return count;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGChoirOfferComponent>();
        while (query.MoveNext(out var uid, out var offer))
        {
            if (now >= offer.Expires)
                RemCompDeferred<WGChoirOfferComponent>(uid);
        }

        // The song keeps its dead: a member who dies turns Remade, and the player goes with the body.
        var members = EntityQueryEnumerator<WGChoirMemberComponent>();
        while (members.MoveNext(out var uid, out var member))
        {
            if (!_mobState.IsDead(uid))
            {
                member.DiedAt = null;
                member.Turning = false;
                continue;
            }

            member.DiedAt ??= now;
            // A conversion that was stopped can start again.
            if (member.Turning && !HasComp<WGRemadeConvertingComponent>(uid))
                member.Turning = false;

            if (now - member.DiedAt >= TurnAfter && !member.Turning)
                member.Turning = _rising.Raise(uid, false);
        }
    }

    /// <summary>Scrawls glyphs on the PDA a new member carries, so it reads as Choir to anyone who looks.</summary>
    private void MarkPda(EntityUid mob)
    {
        if (_inventory.TryGetSlotEntity(mob, "id", out var pda))
            EnsureComp<WGChoirMarkComponent>(pda.Value);
    }

    private void OnAnswer(Entity<WGChoirOfferComponent> ent, ref WGChoirAnswerEvent args)
    {
        args.Handled = true;
        // Critical can still answer; the dead can't.
        if (_mobState.IsDead(ent.Owner))
            return;

        // Several offers can be open at once, so the cap is checked again on answering.
        if (Members() >= _config.GetCVar(WGChoirCVars.MaxMembers))
        {
            if (_player.TryGetSessionByEntity(ent.Owner, out var session))
                _popup.PopupEntity(Loc.GetString("wg-choir-full"), ent.Owner, session, PopupType.Medium);

            RemComp<WGChoirOfferComponent>(ent.Owner);
            return;
        }

        if (!_player.TryGetSessionByEntity(ent.Owner, out var answering))
            return;

        // Two asks, both plain about the cost.
        var mob = ent.Owner;
        _eui.OpenEui(new WolfmedChoiceEui(new WolfmedChoiceEuiState(
            Loc.GetString("wg-choir-confirm-title"),
            Loc.GetString("wg-choir-confirm-text"),
            Loc.GetString("wg-choir-confirm-accept"),
            Loc.GetString("wg-choir-confirm-deny")), first =>
        {
            if (!first || !HasComp<WGChoirOfferComponent>(mob))
                return;

            _eui.OpenEui(new WolfmedChoiceEui(new WolfmedChoiceEuiState(
                Loc.GetString("wg-choir-confirm-final-title"),
                Loc.GetString("wg-choir-confirm-final-text"),
                Loc.GetString("wg-choir-confirm-final-accept"),
                Loc.GetString("wg-choir-confirm-deny")), second =>
            {
                if (!second || !HasComp<WGChoirOfferComponent>(mob) || _mobState.IsDead(mob))
                    return;

                // Others may have joined while the dialogs were open.
                if (Members() >= _config.GetCVar(WGChoirCVars.MaxMembers))
                {
                    _popup.PopupEntity(Loc.GetString("wg-choir-full"), mob, mob, PopupType.Medium);
                    RemComp<WGChoirOfferComponent>(mob);
                    return;
                }

                Answer(mob);
            }), answering);
        }), answering);
    }

    /// <summary>Rolls the answer: half join the Choir, half die where they stand and rise as Remade.</summary>
    public void Answer(EntityUid mob)
    {
        Answer(mob, _random.Prob(AbominationChance));
    }

    /// <summary>Answers with a known outcome. A body that can't turn Remade, such as a cyborg's, always joins.</summary>
    public void Answer(EntityUid mob, bool abomination)
    {
        if (!abomination || !_conversion.IsTurnable(mob))
        {
            Join(mob);
            return;
        }

        RemComp<WGChoirOfferComponent>(mob);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(mob):player} answered the Choir and is turning Remade");
        _chat.SendAdminAlert(mob, Loc.GetString("wg-choir-admin-abomination"));
        if (_player.TryGetSessionByEntity(mob, out var session))
            _popup.PopupEntity(Loc.GetString("wg-choir-abomination-popup"), mob, session, PopupType.LargeCaution);

        _mobState.ChangeMobState(mob, MobState.Dead);
        _rising.Raise(mob, false);
    }

    private void OnOfferShutdown(Entity<WGChoirOfferComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Action is { } action)
            _actions.RemoveAction(ent.Owner, action);
    }

    private void OnMemberShutdown(Entity<WGChoirMemberComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (TryComp<NpcFactionMemberComponent>(ent.Owner, out var member))
            _factions.RemoveFaction((ent.Owner, member), Faction);

        // Taken out of the Choir, they can be revived again.
        if (TryComp<UnrevivableComponent>(ent.Owner, out var unrevivable) && unrevivable.ReasonMessage == "wg-choir-unrevivable")
            RemComp<UnrevivableComponent>(ent.Owner);
    }

    private void OnMarkExamined(Entity<WGChoirMarkComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-choir-pda-mark"), -5);
    }

    private void OnDefconGather(WGDefconGatherEvent ev)
    {
        var members = Members();
        if (members > 0)
            ev.Add(members * 2f, Loc.GetString("wg-choir-defcon-reason", ("count", members)));
    }

    private void Tell(ICommonSession session, string message)
    {
        var wrapped = Loc.GetString("wg-choir-wrap", ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, EntityUid.Invalid, false, session.Channel);
    }
}
