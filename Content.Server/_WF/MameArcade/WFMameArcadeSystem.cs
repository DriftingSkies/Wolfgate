using Content.Shared._WF.MameArcade;
using Content.Shared.ActionBlocker;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.MameArcade;

/// <summary>
/// Seats two players at a MAME cabinet and passes each one's controls to the other, and relays the shown game's sound
/// to people nearby and its picture to watchers. The games run on the clients; the server never sees ROM data, only
/// which set and BIOS each copy uses.
/// </summary>
public sealed partial class WFMameArcadeSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    // Neo Geo frames, about 59.19 a second.
    private const float FrameMilliseconds = 1000f / 59.19f;

    // Beyond ping, an input waits up to a server tick to be relayed and a client frame to be read.
    private const float RelayMilliseconds = 50f;

    // Frames of slack on top of the trip between the two clients; the clients raise it if they still stall.
    private const int DelaySlack = 1;
    private const int MinDelay = 2;
    private const int MaxDelay = 20;

    // Seconds of picture the player may send at once, so a keyframe fits on top of the steady rate.
    private const int VideoBurstSeconds = 2;

    // After a dropped picture packet, how long before another drop asks for a keyframe again.
    private static readonly TimeSpan ResyncCooldown = TimeSpan.FromSeconds(1);

    private readonly List<INetChannel> _recipients = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFMameArcadeComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        // Server systems start before the server takes connections, so the messages are known when clients join.
        _net.RegisterNetMessage<MsgMameArcadeInput>(OnInput);
        _net.RegisterNetMessage<MsgMameArcadeStream>(OnStream);

        Subs.BuiEvents<WFMameArcadeComponent>(MameArcadeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<BoundUIClosedEvent>(OnClosed);
            subs.Event<MameArcadeHostMessage>(OnHost);
            subs.Event<MameArcadeJoinMessage>(OnJoin);
            subs.Event<MameArcadeLeaveMessage>(OnLeave);
            subs.Event<MameArcadeSoloMessage>(OnSolo);
            subs.Event<MameArcadeWatchMessage>(OnWatch);
        });
    }

    /// <summary>Offers a 2-player game at the cabinet. False if one is already open or someone is playing.</summary>
    public bool TryHost(Entity<WFMameArcadeComponent> cabinet, EntityUid player, string game, string fingerprint)
    {
        var comp = cabinet.Comp;
        if (comp.Host != null
            || comp.Player != null
            || game.Length is 0 or > MameArcadeProtocol.MaxGameName
            || fingerprint.Length is 0 or > MameArcadeProtocol.MaxFingerprint)
        {
            return false;
        }

        comp.Host = player;
        comp.Guest = null;
        comp.Game = game;
        comp.Fingerprint = fingerprint;
        UpdateState(cabinet);
        return true;
    }

    /// <summary>Joins the open game as player 2 and starts it for both, on the cabinet's screen. False unless the ROMs match.</summary>
    public bool TryJoin(Entity<WFMameArcadeComponent> cabinet, EntityUid player, string fingerprint)
    {
        var comp = cabinet.Comp;
        if (comp.Host is not { } host || comp.Guest != null || host == player || fingerprint != comp.Fingerprint)
            return false;

        comp.Guest = player;
        Show(comp, host, comp.Game);
        AddSpectators(cabinet);
        var delay = InputDelay(host, player);
        _ui.ServerSendUiMessage(cabinet.Owner, MameArcadeUiKey.Key, new MameArcadeStartMessage(0, delay, Name(player)), host);
        _ui.ServerSendUiMessage(cabinet.Owner, MameArcadeUiKey.Key, new MameArcadeStartMessage(1, delay, Name(host)), player);
        UpdateState(cabinet);
        return true;
    }

    /// <summary>Takes a player out of the lobby, their game or the audience; a partner plays on alone.</summary>
    public void Leave(Entity<WFMameArcadeComponent> cabinet, EntityUid player)
    {
        var comp = cabinet.Comp;
        comp.Watchers.Remove(player);

        if (player == comp.Host || player == comp.Guest)
        {
            var partner = player == comp.Host ? comp.Guest : comp.Host;
            if (partner != null)
            {
                _ui.ServerSendUiMessage(cabinet.Owner, MameArcadeUiKey.Key, new MameArcadePartnerLeftMessage(), partner.Value);
                if (comp.Player == player)
                    Show(comp, partner.Value, comp.Game);
            }

            comp.Host = null;
            comp.Guest = null;
            comp.Game = string.Empty;
            comp.Fingerprint = string.Empty;
        }

        if (comp.Player == player)
            Show(comp, null, string.Empty);

        UpdateState(cabinet);
    }

    /// <summary>
    /// Shows a 1-player game on the cabinet. False if another player's game already shows or a 2-player game is
    /// open; one game plays at a time, and everyone else watches.
    /// </summary>
    public bool TryShowSolo(Entity<WFMameArcadeComponent> cabinet, EntityUid player, string game)
    {
        var comp = cabinet.Comp;
        if (game.Length is 0 or > MameArcadeProtocol.MaxGameName
            || comp.Player != null && comp.Player != player
            || comp.Host != null)
        {
            return false;
        }

        Show(comp, player, game);
        AddSpectators(cabinet);
        UpdateState(cabinet);
        return true;
    }

    /// <summary>Starts sending the shown game's picture to a bystander. False if nothing shows or the audience is full.</summary>
    public bool TryWatch(Entity<WFMameArcadeComponent> cabinet, EntityUid player)
    {
        var comp = cabinet.Comp;
        if (comp.Player == null
            || player == comp.Player || player == comp.Host || player == comp.Guest
            || VideoRate() == 0
            || comp.Watchers.Count >= _cfg.GetCVar(WFMameArcadeCVars.MaxWatchers) && !comp.Watchers.Contains(player))
        {
            return false;
        }

        if (comp.Watchers.Add(player))
        {
            comp.WatcherJoins++;
            UpdateState(cabinet);
        }

        return true;
    }

    public void StopWatching(Entity<WFMameArcadeComponent> cabinet, EntityUid player)
    {
        if (cabinet.Comp.Watchers.Remove(player))
            UpdateState(cabinet);
    }

    /// <summary>The other player of a running 2-player game, if <paramref name="player"/> is in one.</summary>
    public EntityUid? Partner(WFMameArcadeComponent comp, EntityUid player)
    {
        if (comp.Host == null || comp.Guest == null)
            return null;

        if (player == comp.Host)
            return comp.Guest;

        return player == comp.Guest ? comp.Host : null;
    }

    /// <summary>Who hears the shown game from the cabinet: everyone in range except its players, who hear their own.</summary>
    public Filter Listeners(Entity<WFMameArcadeComponent> cabinet)
    {
        var comp = cabinet.Comp;
        return Filter.Empty()
            .AddInRange(_transform.GetMapCoordinates(cabinet.Owner), MameArcadeProtocol.HearRange, _players, EntityManager)
            .RemoveWhereAttachedEntity(e => e == comp.Player || e == comp.Host || e == comp.Guest);
    }

    /// <summary>Whether the cabinet ignores this player's buttons, such as a ghost's, so they can only watch.</summary>
    private bool IsSpectator(EntityUid cabinet, EntityUid player) => !_actionBlocker.CanInteract(player, cabinet);

    /// <summary>
    /// Puts everyone with the window open who can only watch into the audience. Their own watch messages never reach
    /// this system, since the UI drops every message but opening from someone who can't interact.
    /// </summary>
    private void AddSpectators(Entity<WFMameArcadeComponent> cabinet)
    {
        foreach (var actor in _ui.GetActors(cabinet.Owner, MameArcadeUiKey.Key))
        {
            if (IsSpectator(cabinet, actor))
                TryWatch(cabinet, actor);
        }
    }

    private void OnOpened(Entity<WFMameArcadeComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateState(ent);
        AddSpectators(ent);
    }

    private void OnClosed(Entity<WFMameArcadeComponent> ent, ref BoundUIClosedEvent args)
    {
        Leave(ent, args.Actor);
    }

    private void OnHost(Entity<WFMameArcadeComponent> ent, ref MameArcadeHostMessage args)
    {
        TryHost(ent, args.Actor, args.Game, args.Fingerprint);
    }

    private void OnJoin(Entity<WFMameArcadeComponent> ent, ref MameArcadeJoinMessage args)
    {
        TryJoin(ent, args.Actor, args.Fingerprint);
    }

    private void OnLeave(Entity<WFMameArcadeComponent> ent, ref MameArcadeLeaveMessage args)
    {
        Leave(ent, args.Actor);
    }

    private void OnSolo(Entity<WFMameArcadeComponent> ent, ref MameArcadeSoloMessage args)
    {
        TryShowSolo(ent, args.Actor, args.Game);
    }

    private void OnWatch(Entity<WFMameArcadeComponent> ent, ref MameArcadeWatchMessage args)
    {
        if (args.Watching)
            TryWatch(ent, args.Actor);
        else
            StopWatching(ent, args.Actor);
    }

    /// <summary>Passes a seated player's inputs straight on to their partner.</summary>
    private void OnInput(MsgMameArcadeInput message)
    {
        if (!_players.TryGetSessionByChannel(message.MsgChannel, out var session)
            || session.AttachedEntity is not { } player
            || !TryGetEntity(message.Cabinet, out var cabinet)
            || !TryComp<WFMameArcadeComponent>(cabinet, out var comp)
            || Partner(comp, player) is not { } partner
            || !TryComp<ActorComponent>(partner, out var actor)
            || message.Inputs.Length == 0
            || message.FirstFrame < 0
            || !TrySpendInput(comp, player == comp.Host, message.Inputs.Length + 1))
        {
            return;
        }

        _net.ServerSendMessage(new MsgMameArcadeInput
        {
            Cabinet = message.Cabinet,
            FirstFrame = message.FirstFrame,
            Inputs = message.Inputs,
            ChecksumFrame = message.ChecksumFrame,
            Checksum = message.Checksum,
            Margin = message.Margin,
            Delay = message.Delay,
        }, actor.PlayerSession.Channel);
    }

    /// <summary>Passes the shown game's sound to people nearby, or its picture to the watchers.</summary>
    private void OnStream(MsgMameArcadeStream message)
    {
        if (!_players.TryGetSessionByChannel(message.MsgChannel, out var session)
            || session.AttachedEntity is not { } player
            || !TryGetEntity(message.Cabinet, out var cabinet)
            || !TryComp<WFMameArcadeComponent>(cabinet, out var comp)
            || comp.Player != player
            || message.Data.Length == 0
            || !_ui.IsUiOpen(cabinet.Value, MameArcadeUiKey.Key, player))
        {
            return;
        }

        if (!TrySpend(comp, message.Kind, message.Data.Length))
        {
            // Watchers' screens are built from changes, so a lost one leaves them wrong until a keyframe.
            if (message.Kind == MameArcadeStreamKind.Video)
                RequestKeyframe((cabinet.Value, comp));

            return;
        }

        _recipients.Clear();
        if (message.Kind == MameArcadeStreamKind.Audio)
        {
            foreach (var listener in Listeners((cabinet.Value, comp)).Recipients)
            {
                _recipients.Add(listener.Channel);
            }
        }
        else
        {
            foreach (var watcher in comp.Watchers)
            {
                if (TryComp<ActorComponent>(watcher, out var actor))
                    _recipients.Add(actor.PlayerSession.Channel);
            }
        }

        if (_recipients.Count == 0)
            return;

        _net.ServerSendToMany(new MsgMameArcadeStream
        {
            Cabinet = message.Cabinet,
            Kind = message.Kind,
            Data = message.Data,
        }, _recipients);
    }

    /// <summary>Charges a packet to the cabinet's allowance, refilled each second. False if it's spent.</summary>
    private bool TrySpend(WFMameArcadeComponent comp, MameArcadeStreamKind kind, int bytes)
    {
        var now = _timing.RealTime;
        if (now >= comp.AllowanceRefill)
        {
            comp.AllowanceRefill = now + TimeSpan.FromSeconds(1);
            comp.AudioAllowance = MameArcadeProtocol.MaxAudioBytesPerSecond;
            comp.VideoAllowance = Math.Min(comp.VideoAllowance + VideoRate(), VideoRate() * VideoBurstSeconds);
        }

        if (kind == MameArcadeStreamKind.Audio)
        {
            if (comp.AudioAllowance < bytes)
                return false;

            comp.AudioAllowance -= bytes;
            return true;
        }

        if (kind != MameArcadeStreamKind.Video || comp.Watchers.Count == 0 || comp.VideoAllowance < bytes)
            return false;

        comp.VideoAllowance -= bytes;
        return true;
    }

    /// <summary>Charges a seat's input message to its allowance, refilled each second. False if it's spent.</summary>
    private bool TrySpendInput(WFMameArcadeComponent comp, bool host, int cost)
    {
        var now = _timing.RealTime;
        if (now >= comp.InputRefill)
        {
            comp.InputRefill = now + TimeSpan.FromSeconds(1);
            comp.HostInputAllowance = MameArcadeProtocol.MaxInputCostPerSecond;
            comp.GuestInputAllowance = MameArcadeProtocol.MaxInputCostPerSecond;
        }

        ref var allowance = ref host ? ref comp.HostInputAllowance : ref comp.GuestInputAllowance;
        if (allowance < cost)
            return false;

        allowance -= cost;
        return true;
    }

    /// <summary>Asks the player for a keyframe, at most once per <see cref="ResyncCooldown"/>.</summary>
    private void RequestKeyframe(Entity<WFMameArcadeComponent> cabinet)
    {
        var now = _timing.RealTime;
        if (cabinet.Comp.Watchers.Count == 0 || now < cabinet.Comp.NextResync)
            return;

        cabinet.Comp.NextResync = now + ResyncCooldown;
        cabinet.Comp.WatcherJoins++;
        UpdateState(cabinet);
    }

    /// <summary>Picture bytes a second per watcher; 0 turns watching off.</summary>
    private int VideoRate() => Math.Max(_cfg.GetCVar(WFMameArcadeCVars.VideoRate), 0) * 1024;

    /// <summary>
    /// Puts a player's game on the cabinet. The audience stays when the same game passes to the partner, and
    /// otherwise starts empty.
    /// </summary>
    private void Show(WFMameArcadeComponent comp, EntityUid? player, string game)
    {
        if (player == null || comp.PlayerGame != game)
            comp.Watchers.Clear();

        comp.Player = player;
        comp.PlayerGame = player == null ? string.Empty : game;
    }

    /// <summary>
    /// Closes the cabinet for a player who goes into crit or dies, so they stop playing or watching. Their controls and
    /// streams are raw net messages, which the UI's interaction checks never see.
    /// </summary>
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        var query = EntityQueryEnumerator<WFMameArcadeComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_ui.IsUiOpen(uid, MameArcadeUiKey.Key, args.Target))
                _ui.CloseUi(uid, MameArcadeUiKey.Key, args.Target);
        }
    }

    private void OnPowerChanged(Entity<WFMameArcadeComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;

        _ui.CloseUi(ent.Owner, MameArcadeUiKey.Key);
        Reset(ent);
    }

    private void Reset(Entity<WFMameArcadeComponent> cabinet)
    {
        var comp = cabinet.Comp;
        comp.Host = null;
        comp.Guest = null;
        comp.Game = string.Empty;
        comp.Fingerprint = string.Empty;
        Show(comp, null, string.Empty);
        UpdateState(cabinet);
    }

    private void UpdateState(Entity<WFMameArcadeComponent> cabinet)
    {
        var comp = cabinet.Comp;
        var showing = new MameArcadeShowing(
            GetNetEntity(comp.Player),
            comp.Player is { } player ? Name(player) : string.Empty,
            comp.PlayerGame,
            comp.Watchers.Count,
            comp.WatcherJoins,
            _cfg.GetCVar(WFMameArcadeCVars.MaxWatchers),
            VideoRate());

        var state = new MameArcadeBuiState(
            GetNetEntity(comp.Host),
            comp.Host is { } host ? Name(host) : string.Empty,
            comp.Game,
            comp.Fingerprint,
            GetNetEntity(comp.Guest),
            comp.Guest is { } guest ? Name(guest) : string.Empty,
            showing);

        _ui.SetUiState(cabinet.Owner, MameArcadeUiKey.Key, state);
    }

    /// <summary>Enough frames for one player's input to reach the other through the server.</summary>
    private int InputDelay(EntityUid first, EntityUid second)
    {
        var trip = (Ping(first) + Ping(second)) / 2f + RelayMilliseconds;
        return Math.Clamp((int) MathF.Ceiling(trip / FrameMilliseconds) + DelaySlack, MinDelay, MaxDelay);
    }

    private int Ping(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor) ? actor.PlayerSession.Channel.Ping : 0;
    }

    private string Name(EntityUid player) => Identity.Name(player, EntityManager);
}
