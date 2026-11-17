using System.Linq;
using Content.Server._WG.Audio.InternetSound;
using Content.Server._WG.ShipPa;
using Content.Shared._WG.Audio.InternetSound;
using Content.Shared._WG.Boombox;
using Content.Shared._WG.CCVar;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.Boombox;

/// <summary>
/// The boombox's playlist: links queue on the box and play one after another out of it through the internet
/// sound system. A playlist link adds up to <see cref="WGBoomboxComponent.MaxQueued"/> playable tracks.
/// </summary>
public sealed partial class WGBoomboxSystem : EntitySystem
{
    [Dependency] private InternetSoundSystem _internetSound = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan SkipCooldown = TimeSpan.FromSeconds(3);

    /// <summary>Quietest the volume knob goes before it is off.</summary>
    private const float MinGain = 0.02f;

    private TimeSpan _nextUpdate;
    private int _nextId = 1;
    private readonly List<EntityUid> _boxes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGBoomboxComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WGBoomboxComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGBoomboxComponent, InternetSoundSourceChangedEvent>(OnSourceChanged);
        SubscribeLocalEvent<WGBoomboxComponent, ComponentShutdown>(OnShutdown);

        Subs.BuiEvents<WGBoomboxComponent>(WGBoomboxUiKey.Key, subs =>
        {
            subs.Event<WGBoomboxAddMessage>(OnAdd);
            subs.Event<WGBoomboxSkipMessage>(OnSkip);
            subs.Event<WGBoomboxStopMessage>(OnStop);
            subs.Event<WGBoomboxClearMessage>(OnClear);
            subs.Event<WGBoomboxShuffleMessage>(OnShuffle);
            subs.Event<WGBoomboxRepeatMessage>(OnRepeat);
            subs.Event<WGBoomboxRemoveMessage>(OnRemove);
            subs.Event<WGBoomboxMoveUpMessage>(OnMoveUp);
            subs.Event<WGBoomboxVolumeMessage>(OnVolume);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        _boxes.Clear();
        var query = EntityQueryEnumerator<WGBoomboxComponent>();
        while (query.MoveNext(out var uid, out var box))
        {
            if (box.Playing != null || box.Links.Count > 0)
                _boxes.Add(uid);
        }

        foreach (var uid in _boxes)
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out WGBoomboxComponent? box))
                continue;

            if (_internetSound.IsPlayingFrom(uid))
            {
                RefreshTitle((uid, box));
                continue;
            }

            // Whatever was playing is over: it finished, failed or was stopped from elsewhere.
            Finish((uid, box));
            Advance((uid, box));
        }

        _boxes.Clear();
    }

    /// <summary>True when a new link would play at once rather than wait.</summary>
    public bool IsIdle(Entity<WGBoomboxComponent> ent) =>
        !_internetSound.IsPlayingFrom(ent) && ent.Comp.Links.Count == 0 && ent.Comp.Playing == null;

    /// <summary>
    /// Plays a link at once on a quiet box, otherwise queues it. A playlist link is read first and adds its
    /// playable tracks. Error is a loc string for the requester when this returns false.
    /// </summary>
    public bool TryAdd(Entity<WGBoomboxComponent> ent, string url, string requester, ICommonSession? session, out string? error)
    {
        error = null;

        if (!_cfg.GetCVar(InternetSoundCVars.PlayerRequests))
        {
            error = Loc.GetString("wg-boombox-disabled");
            return false;
        }

        url = url.Trim();
        if (url.Length == 0 || url.Length > ent.Comp.MaxUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = Loc.GetString("wg-internet-sound-invalid-url");
            return false;
        }

        var idle = IsIdle(ent);
        if (idle && _timing.CurTime < ent.Comp.NextRequest)
        {
            var seconds = (int) Math.Ceiling((ent.Comp.NextRequest - _timing.CurTime).TotalSeconds);
            error = Loc.GetString("wg-boombox-cooldown", ("seconds", seconds));
            return false;
        }

        var room = WGBoomboxComponent.MaxQueued - ent.Comp.Links.Count + (idle ? 1 : 0);
        if (room <= 0)
        {
            error = Loc.GetString("wg-boombox-full");
            return false;
        }

        if (InternetSoundDownloader.IsPlaylistLink(uri))
            return TryReadPlaylist(ent, uri.AbsoluteUri, Math.Min(room, WGBoomboxComponent.MaxQueued), requester, session, out error);

        var link = NewLink(uri.AbsoluteUri, ShipPaPlaylistText.ShortenLink(uri.AbsoluteUri), 0, requester, session);

        if (!idle)
        {
            ent.Comp.Links.Add(link);
            Sync(ent);
            return true;
        }

        if (!Start(ent, link, out error))
            return false;

        ent.Comp.NextRequest = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(InternetSoundCVars.RequestCooldown));
        return true;
    }

    private bool TryReadPlaylist(Entity<WGBoomboxComponent> ent, string url, int max, string requester, ICommonSession? session, out string? error)
    {
        error = null;
        if (ent.Comp.Reading)
        {
            error = Loc.GetString("wg-boombox-reading");
            return false;
        }

        ent.Comp.Reading = true;
        Dirty(ent);

        var uid = ent.Owner;
        _internetSound.ExpandPlaylist(url, max, (result, failure) =>
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out WGBoomboxComponent? box))
                return;

            box.Reading = false;
            Entity<WGBoomboxComponent> target = (uid, box);

            if (result == null)
            {
                Tell(session, failure ?? Loc.GetString("wg-boombox-refused"));
                Sync(target, true);
                return;
            }

            var room = WGBoomboxComponent.MaxQueued - box.Links.Count + (IsIdle(target) ? 1 : 0);
            var added = 0;
            foreach (var entry in result.Entries)
            {
                if (added >= room)
                    break;

                box.Links.Add(NewLink(entry.Url, ShipPaPlaylistText.Label(entry.Title), entry.Seconds, requester, session));
                added++;
            }

            Tell(session, Loc.GetString("wg-boombox-playlist-added",
                ("count", added), ("skipped", result.Skipped + result.Entries.Count - added)));

            if (added > 0 && box.Playing == null && !_internetSound.IsPlayingFrom(uid))
                box.NextRequest = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(InternetSoundCVars.RequestCooldown));

            Advance(target);
            Sync(target, true);
        });

        return true;
    }

    private WGBoomboxLink NewLink(string url, string label, int seconds, string requester, ICommonSession? session)
    {
        return new WGBoomboxLink
        {
            Id = _nextId++,
            Url = url,
            Label = label,
            Seconds = seconds,
            Requester = requester,
            Session = session,
        };
    }

    private bool Start(Entity<WGBoomboxComponent> ent, WGBoomboxLink link, out string? error)
    {
        error = null;
        if (!TryComp(ent, out InternetSoundSourceComponent? source)
            || !_internetSound.PlayFromSource(link.Requester, link.Url, (ent, source), out error, link.Session))
            return false;

        ent.Comp.Playing = link;
        Sync(ent);
        return true;
    }

    /// <summary>The playing track is done; with repeat on it goes back to the end of the queue.</summary>
    private void Finish(Entity<WGBoomboxComponent> ent)
    {
        if (ent.Comp.Playing is not { } done)
            return;

        ent.Comp.Playing = null;
        if (ent.Comp.Repeat && ent.Comp.Links.Count < WGBoomboxComponent.MaxQueued)
            ent.Comp.Links.Add(done);
    }

    /// <summary>
    /// Starts the first waiting link that will play. A refused link is dropped and its requester told why;
    /// at the server's track cap the queue waits.
    /// </summary>
    private void Advance(Entity<WGBoomboxComponent> ent)
    {
        while (ent.Comp.Playing == null && ent.Comp.Links.Count > 0
               && !_internetSound.IsPlayingFrom(ent) && _internetSound.HasFreeSlot(ent))
        {
            var next = ent.Comp.Links[0];
            ent.Comp.Links.RemoveAt(0);

            string? error = null;
            if (!_cfg.GetCVar(InternetSoundCVars.PlayerRequests))
                error = Loc.GetString("wg-boombox-disabled");
            else if (Start(ent, next, out error))
                break;

            Log.Info($"Dropped boombox link from {next.Requester}: {error}");
            Tell(next.Session, error ?? Loc.GetString("wg-boombox-refused"));
        }

        Sync(ent);
    }

    private void Tell(ICommonSession? session, string message)
    {
        if (session is { Status: SessionStatus.InGame })
            _popup.PopupCursor(message, session, PopupType.Medium);
    }

    private void RefreshTitle(Entity<WGBoomboxComponent> ent)
    {
        if (ent.Comp.Playing is not { } playing
            || !TryComp(ent, out InternetSoundSourceComponent? source)
            || string.IsNullOrWhiteSpace(source.Title))
            return;

        var label = ShipPaPlaylistText.Label(source.Title);
        var seconds = (int) Math.Ceiling(source.Length);
        if (label == playing.Label && seconds == playing.Seconds)
            return;

        playing.Label = label;
        playing.Seconds = seconds;
        Sync(ent);
    }

    /// <summary>Copies the server's lists into the networked labels.</summary>
    private void Sync(Entity<WGBoomboxComponent> ent, bool force = false)
    {
        WGBoomboxEntry? current = ent.Comp.Playing is { } playing
            ? new WGBoomboxEntry(playing.Id, playing.Label, playing.Seconds)
            : null;
        var queue = ent.Comp.Links.Select(link => new WGBoomboxEntry(link.Id, link.Label, link.Seconds)).ToList();

        if (!force && current == ent.Comp.Current && queue.SequenceEqual(ent.Comp.Queue))
            return;

        ent.Comp.Current = current;
        ent.Comp.Queue = queue;
        Dirty(ent);
    }

    private void OnAdd(Entity<WGBoomboxComponent> ent, ref WGBoomboxAddMessage args)
    {
        _players.TryGetSessionByEntity(args.Actor, out var session);
        if (!TryAdd(ent, args.Url, Name(args.Actor), session, out var error))
            _popup.PopupEntity(error ?? Loc.GetString("wg-boombox-refused"), ent, args.Actor);
    }

    private void OnSkip(Entity<WGBoomboxComponent> ent, ref WGBoomboxSkipMessage args)
    {
        if (_timing.CurTime < ent.Comp.NextSkip)
            return;

        ent.Comp.NextSkip = _timing.CurTime + SkipCooldown;
        _internetSound.StopForSource(ent);
        Finish(ent);
        Advance(ent);
    }

    private void OnStop(Entity<WGBoomboxComponent> ent, ref WGBoomboxStopMessage args)
    {
        Stop(ent);
    }

    /// <summary>Ends the playing track and empties the queue.</summary>
    public void Stop(Entity<WGBoomboxComponent> ent)
    {
        ent.Comp.Links.Clear();
        ent.Comp.Playing = null;
        _internetSound.StopForSource(ent);
        Sync(ent);
    }

    private void OnClear(Entity<WGBoomboxComponent> ent, ref WGBoomboxClearMessage args)
    {
        ent.Comp.Links.Clear();
        Sync(ent);
    }

    private void OnShuffle(Entity<WGBoomboxComponent> ent, ref WGBoomboxShuffleMessage args)
    {
        _random.Shuffle(ent.Comp.Links);
        Sync(ent);
    }

    private void OnRepeat(Entity<WGBoomboxComponent> ent, ref WGBoomboxRepeatMessage args)
    {
        ent.Comp.Repeat = args.Repeat;
        Dirty(ent);
    }

    private void OnRemove(Entity<WGBoomboxComponent> ent, ref WGBoomboxRemoveMessage args)
    {
        var id = args.Id;
        if (ent.Comp.Links.RemoveAll(link => link.Id == id) > 0)
            Sync(ent);
    }

    private void OnMoveUp(Entity<WGBoomboxComponent> ent, ref WGBoomboxMoveUpMessage args)
    {
        var id = args.Id;
        var index = ent.Comp.Links.FindIndex(link => link.Id == id);
        if (index <= 0)
            return;

        (ent.Comp.Links[index - 1], ent.Comp.Links[index]) = (ent.Comp.Links[index], ent.Comp.Links[index - 1]);
        Sync(ent);
    }

    private void OnVolume(Entity<WGBoomboxComponent> ent, ref WGBoomboxVolumeMessage args)
    {
        if (!float.IsFinite(args.Volume) || !TryComp(ent, out InternetSoundSourceComponent? source))
            return;

        var gain = Math.Clamp(args.Volume, 0f, 1f);
        source.Volume = gain < MinGain ? -80f : SharedAudioSystem.GainToVolume(gain);
        Dirty(ent, source);
    }

    private void OnGetVerbs(Entity<WGBoomboxComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || IsIdle(ent))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wg-boombox-stop"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/close.svg.192dpi.png")),
            Act = () => Stop(ent),
        });
    }

    private void OnExamined(Entity<WGBoomboxComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (TryComp(ent, out InternetSoundSourceComponent? source) && source.IsPlaying(_timing.CurTime) && source.Title != null)
            args.PushMarkup(Loc.GetString("wg-boombox-examine-playing", ("title", FormattedMessage.EscapeText(source.Title))));
        else if (_internetSound.IsPlayingFrom(ent))
            args.PushMarkup(Loc.GetString("wg-boombox-examine-fetching"));
    }

    private void OnSourceChanged(Entity<WGBoomboxComponent> ent, ref InternetSoundSourceChangedEvent args)
    {
        _appearance.SetData(ent, WGBoomboxVisuals.Playing, args.Playing);
    }

    private void OnShutdown(Entity<WGBoomboxComponent> ent, ref ComponentShutdown args)
    {
        _internetSound.StopForSource(ent);
    }
}
