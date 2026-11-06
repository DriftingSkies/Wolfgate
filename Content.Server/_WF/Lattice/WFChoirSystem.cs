using Content.Server._WF.Defcon;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._WF.Lattice;
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

namespace Content.Server._WF.Lattice;

/// <summary>
/// The Choir, as players. Someone whose Drift runs very high hears the song once a round and gets an action to answer
/// it; nobody joins without using it. A Choir player takes the Choir's faction, stops hallucinating, is briefed on
/// what the Choir wants, and counts towards DEFCON. Admins are told of each offer and each join. Population and member
/// caps keep it out of quiet rounds.
/// </summary>
public sealed partial class WFChoirSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public const string Faction = "WFChoir";
    private const string ActionPrototype = "WFChoirActionAnswer";

    /// <summary>How long the offer stays open.</summary>
    private static readonly TimeSpan OfferTime = TimeSpan.FromMinutes(5);

    private readonly HashSet<NetUserId> _offered = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _offered.Clear());
        SubscribeLocalEvent<WFChoirOfferComponent, WFChoirAnswerEvent>(OnAnswer);
        SubscribeLocalEvent<WFChoirOfferComponent, ComponentShutdown>(OnOfferShutdown);
        SubscribeLocalEvent<WFChoirMemberComponent, ComponentShutdown>(OnMemberShutdown);
        SubscribeLocalEvent<WFDefconGatherEvent>(OnDefconGather);
    }

    /// <summary>Offers the song to a player deep enough in Drift, if the round allows it and they haven't had it.</summary>
    public void Consider(ICommonSession session, EntityUid mob, float drift)
    {
        if (drift < _config.GetCVar(WFChoirCVars.MinDrift)
            || !_config.GetCVar(WFChoirCVars.Enabled)
            || _mobState.IsDead(mob)
            || HasComp<WFChoirMemberComponent>(mob)
            || HasComp<WFChoirOfferComponent>(mob)
            || _offered.Contains(session.UserId)
            || _player.PlayerCount < _config.GetCVar(WFChoirCVars.MinPlayers)
            || Members() >= _config.GetCVar(WFChoirCVars.MaxMembers))
            return;

        Offer(session, mob);
    }

    /// <summary>Gives a player the offer and its action, whatever the caps say.</summary>
    public void Offer(ICommonSession session, EntityUid mob)
    {
        _offered.Add(session.UserId);
        var offer = EnsureComp<WFChoirOfferComponent>(mob);
        offer.Expires = _timing.CurTime + OfferTime;
        offer.Action = _actions.AddAction(mob, ActionPrototype);
        _popup.PopupEntity(Loc.GetString("wf-choir-offer-popup"), mob, session, PopupType.LargeCaution);
        Tell(session, Loc.GetString("wf-choir-offer"));
        _chat.SendAdminAlert(mob, Loc.GetString("wf-choir-admin-offered"));
    }

    /// <summary>Makes someone Choir.</summary>
    public void Join(EntityUid mob)
    {
        RemComp<WFChoirOfferComponent>(mob);
        if (HasComp<WFChoirMemberComponent>(mob))
            return;

        EnsureComp<WFChoirMemberComponent>(mob);
        _factions.AddFaction((mob, null), Faction);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(mob):player} joined the Choir");
        _chat.SendAdminAlert(mob, Loc.GetString("wf-choir-admin-joined"));
        if (_player.TryGetSessionByEntity(mob, out var session))
        {
            _popup.PopupEntity(Loc.GetString("wf-choir-joined-popup"), mob, session, PopupType.LargeCaution);
            Tell(session, Loc.GetString("wf-choir-briefing"));
        }
    }

    /// <summary>How many living Choir members a player still controls; dead bodies and abandoned ones don't count.</summary>
    public int Members()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFChoirMemberComponent>();
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
        var query = EntityQueryEnumerator<WFChoirOfferComponent>();
        while (query.MoveNext(out var uid, out var offer))
        {
            if (now >= offer.Expires)
                RemCompDeferred<WFChoirOfferComponent>(uid);
        }
    }

    private void OnAnswer(Entity<WFChoirOfferComponent> ent, ref WFChoirAnswerEvent args)
    {
        args.Handled = true;
        // Critical can still answer; the dead can't.
        if (_mobState.IsDead(ent.Owner))
            return;

        // Several offers can be open at once, so the cap is checked again on answering.
        if (Members() >= _config.GetCVar(WFChoirCVars.MaxMembers))
        {
            if (_player.TryGetSessionByEntity(ent.Owner, out var session))
                _popup.PopupEntity(Loc.GetString("wf-choir-full"), ent.Owner, session, PopupType.Medium);

            RemComp<WFChoirOfferComponent>(ent.Owner);
            return;
        }

        Join(ent.Owner);
    }

    private void OnOfferShutdown(Entity<WFChoirOfferComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Action is { } action)
            _actions.RemoveAction(ent.Owner, action);
    }

    private void OnMemberShutdown(Entity<WFChoirMemberComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent.Owner) && TryComp<NpcFactionMemberComponent>(ent.Owner, out var member))
            _factions.RemoveFaction((ent.Owner, member), Faction);
    }

    private void OnDefconGather(WFDefconGatherEvent ev)
    {
        var members = Members();
        if (members > 0)
            ev.Add(members * 2f, Loc.GetString("wf-choir-defcon-reason", ("count", members)));
    }

    private void Tell(ICommonSession session, string message)
    {
        var wrapped = Loc.GetString("wf-choir-wrap", ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, EntityUid.Invalid, false, session.Channel);
    }
}
