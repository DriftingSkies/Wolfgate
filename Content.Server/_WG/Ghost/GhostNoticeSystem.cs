using Content.Server._WG.Lattice;
using Content.Server._WG.Warheads;
using Content.Server.Chat.Managers;
using Content.Shared._WG.Ghost;
using Content.Shared._WG.Warheads;
using Content.Shared._WG.Xenonids.Parasite;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.Ghost;

/// <summary>What a ghost notice is about; picks its heading and colour.</summary>
public enum GhostNoticeKind : byte
{
    Xeno,
    Choir,
    Monolith,
    Nuclear,
    Lattice,
}

/// <summary>
/// Ghost notices, after SS13's ghost alerts: a chip on every ghost's screen and a chat line, both of which orbit the
/// source when clicked. Other systems call <see cref="Notify"/>; the events with a hook of their own are caught here.
/// </summary>
public sealed class GhostNoticeSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>The same notice about the same source is dropped inside this.</summary>
    private static readonly TimeSpan Repeat = TimeSpan.FromSeconds(20);

    private const int RecentCount = 32;

    private readonly Dictionary<(EntityUid, string), TimeSpan> _lastSent = new();
    private readonly HashSet<EntityUid> _bursting = new();
    private readonly List<GhostNoticeEvent> _recent = new();
    private TimeSpan _nextScan;

    /// <summary>The last notices sent, oldest first.</summary>
    public IReadOnlyList<GhostNoticeEvent> Recent => _recent;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoParasiteInfectEvent>(OnInfected);
        SubscribeLocalEvent<WGWarheadArmedEvent>(OnWarheadArmed);
        SubscribeLocalEvent<WGLatticeBreachEvent>(OnBreach);
    }

    /// <summary>Tells every ghost about <paramref name="source"/>, unless the same notice about it went out just now.</summary>
    public void Notify(EntityUid source, GhostNoticeKind kind, string text, float seconds = 30f)
    {
        if (TerminatingOrDeleted(source))
            return;

        var now = _timing.CurTime;
        if (_lastSent.TryGetValue((source, text), out var last) && now < last + Repeat)
            return;

        var ghosts = new List<INetChannel>();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } attached && HasComp<GhostComponent>(attached))
                ghosts.Add(session.Channel);
        }

        // Nobody saw it, so a repeat inside the window still goes out.
        if (ghosts.Count == 0)
            return;

        _lastSent[(source, text)] = now;
        var title = Loc.GetString($"wg-ghost-notice-title-{kind.ToString().ToLowerInvariant()}");
        var color = KindColor(kind);
        var ev = new GhostNoticeEvent
        {
            Source = GetNetEntity(source),
            Title = title,
            Text = text,
            Prototype = MetaData(source).EntityPrototype?.ID,
            Color = color,
            Duration = seconds,
        };

        _recent.Add(ev);
        if (_recent.Count > RecentCount)
            _recent.RemoveAt(0);

        foreach (var channel in ghosts)
            RaiseNetworkEvent(ev, channel);

        var wrapped = Loc.GetString("wg-ghost-notice-chat",
            ("color", color.ToHex()),
            ("title", FormattedMessage.EscapeText(title)),
            ("text", FormattedMessage.EscapeText(text)));
        _chat.ChatMessageToMany(ChatChannel.Server, text, wrapped, source, false, true, ghosts);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextScan)
            return;

        _nextScan = now + TimeSpan.FromSeconds(1);
        PruneSent(now);

        // A larva starting to chew its way out has no event of its own.
        _bursting.RemoveWhere(uid => !TryComp<VictimInfectedComponent>(uid, out var stillInfected) || !stillInfected.IsBursting);
        var query = EntityQueryEnumerator<VictimInfectedComponent>();
        while (query.MoveNext(out var uid, out var infected))
        {
            if (infected.IsBursting && _bursting.Add(uid))
                Notify(uid, GhostNoticeKind.Xeno, Loc.GetString("wg-ghost-notice-burst", ("victim", uid)));
        }
    }

    private void PruneSent(TimeSpan now)
    {
        if (_lastSent.Count < 64)
            return;

        var stale = new List<(EntityUid, string)>();
        foreach (var (key, sent) in _lastSent)
        {
            if (now >= sent + Repeat)
                stale.Add(key);
        }

        foreach (var key in stale)
            _lastSent.Remove(key);
    }

    private void OnInfected(XenoParasiteInfectEvent args)
    {
        Notify(args.Target, GhostNoticeKind.Xeno, Loc.GetString("wg-ghost-notice-infected", ("victim", args.Target)));
    }

    private void OnWarheadArmed(ref WGWarheadArmedEvent args)
    {
        if (!TryComp<WGWarheadComponent>(args.Warhead, out var warhead))
            return;

        var text = warhead.State == WGWarheadState.Armed ? "wg-ghost-notice-warhead-armed" : "wg-ghost-notice-warhead-countdown";
        Notify(args.Warhead, GhostNoticeKind.Nuclear, Loc.GetString(text, ("warhead", args.Warhead)));
    }

    private void OnBreach(ref WGLatticeBreachEvent args)
    {
        Notify(args.Anchor, GhostNoticeKind.Lattice, Loc.GetString("wg-ghost-notice-breach", ("anchor", args.Anchor)), 45f);
    }

    private static Color KindColor(GhostNoticeKind kind)
    {
        return kind switch
        {
            GhostNoticeKind.Xeno => Color.FromHex("#9c4dff"),
            GhostNoticeKind.Choir => Color.FromHex("#d070ff"),
            GhostNoticeKind.Monolith => Color.FromHex("#e04040"),
            GhostNoticeKind.Nuclear => Color.FromHex("#ffb020"),
            _ => Color.FromHex("#d030a0"),
        };
    }
}
