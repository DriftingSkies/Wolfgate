using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WG.Headshot;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.Headshot;

/// <summary>
/// Gives spawned characters their profile headshot and shows it to examiners who can see their face. Images are
/// fetched once per URL and handed to a client only for hashes it was shown.
/// </summary>
public sealed partial class HeadshotSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const int MaxCachedUrls = 256;
    private static readonly TimeSpan PreviewCooldown = TimeSpan.FromSeconds(3);

    private HeadshotFetcher _fetcher = default!;

    /// <summary>Fetches by URL, shared by every character and preview that uses it. Failures are dropped.</summary>
    private readonly Dictionary<string, Task<HeadshotResult>> _fetches = new();

    /// <summary>Hash of each successfully fetched URL.</summary>
    private readonly Dictionary<string, string> _hashes = new();

    private readonly Dictionary<string, byte[]> _images = new();

    /// <summary>Hashes each session was shown and hasn't downloaded yet; the only ones it may request.</summary>
    private readonly Dictionary<ICommonSession, HashSet<string>> _shown = new();

    private readonly Dictionary<ICommonSession, TimeSpan> _nextPreview = new();

    public override void Initialize()
    {
        base.Initialize();

        _fetcher = new HeadshotFetcher(Log);

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<HeadshotComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<HeadshotComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeNetworkEvent<HeadshotRequestEvent>(OnRequest);
        SubscribeNetworkEvent<HeadshotPreviewRequestEvent>(OnPreviewRequest);

        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
        _fetcher.Dispose();
    }

    /// <summary>Whether the face is visible, by the same rule that hides a name behind "Unknown".</summary>
    public bool IsFaceVisible(EntityUid uid)
    {
        var ev = new SeeIdentityAttemptEvent();
        RaiseLocalEvent(uid, ev);
        return !ev.Cancelled;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!_cfg.GetCVar(HeadshotCVars.Enabled) || ev.Profile.HeadshotUrl is not { Length: > 0 } url)
            return;

        var headshot = EnsureComp<HeadshotComponent>(ev.Mob);
        headshot.Url = url;
        headshot.Hash = null;
        _adminLog.Add(LogType.Identity, LogImpact.Low, $"{ToPrettyString(ev.Mob):player} spawned with headshot {url}");
        Load(ev.Mob, url);
    }

    private async void Load(EntityUid uid, string url)
    {
        var result = await GetOrFetch(url);
        if (result.Hash == null || !TryComp<HeadshotComponent>(uid, out var headshot) || headshot.Url != url)
            return;

        headshot.Hash = result.Hash;
    }

    private void OnExamined(Entity<HeadshotComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Hash is not { } hash
            || !args.IsInDetailsRange
            || !_cfg.GetCVar(HeadshotCVars.Enabled)
            || !TryComp<ActorComponent>(args.Examiner, out var actor)
            || !IsFaceVisible(ent))
            return;

        _shown.GetOrNew(actor.PlayerSession).Add(hash);
        RaiseNetworkEvent(new HeadshotShowEvent(GetNetEntity(ent), hash), actor.PlayerSession);
    }

    private void OnRequest(HeadshotRequestEvent msg, EntitySessionEventArgs args)
    {
        if (msg.Hash is not { } hash
            || !_shown.TryGetValue(args.SenderSession, out var shown)
            || !shown.Remove(hash)
            || !_images.TryGetValue(hash, out var png))
            return;

        RaiseNetworkEvent(new HeadshotImageEvent(hash, png), args.SenderSession);
    }

    private async void OnPreviewRequest(HeadshotPreviewRequestEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var url = msg.Url?.Trim() ?? string.Empty;

        if (!_cfg.GetCVar(HeadshotCVars.Enabled))
        {
            RaiseNetworkEvent(new HeadshotPreviewResultEvent(url, null, "wg-headshot-error-disabled"), session);
            return;
        }

        var now = _timing.RealTime;
        if (_nextPreview.TryGetValue(session, out var next) && now < next)
        {
            RaiseNetworkEvent(new HeadshotPreviewResultEvent(url, null, "wg-headshot-error-cooldown"), session);
            return;
        }

        _nextPreview[session] = now + PreviewCooldown;
        var result = await GetOrFetch(url);
        if (session.Status != SessionStatus.Disconnected)
            RaiseNetworkEvent(new HeadshotPreviewResultEvent(url, result.Png, result.Error), session);
    }

    private Task<HeadshotResult> GetOrFetch(string url)
    {
        if (_fetches.TryGetValue(url, out var existing))
            return existing;

        if (!HeadshotRules.IsValid(url, out var reason))
            return Task.FromResult(HeadshotResult.Fail(reason));

        var uri = new Uri(url);
        if (!HeadshotRules.IsHostAllowed(uri.Host, _cfg.GetCVar(HeadshotCVars.AllowedHosts)))
            return Task.FromResult(HeadshotResult.Fail("wg-headshot-error-not-allowed"));

        if (_fetches.Count >= MaxCachedUrls)
            TrimCache();

        var task = FetchAndStore(url, uri);
        _fetches[url] = task;
        return task;
    }

    private async Task<HeadshotResult> FetchAndStore(string url, Uri uri)
    {
        HeadshotResult result;
        try
        {
            result = await _fetcher.FetchAsync(uri, _cfg.GetCVar(HeadshotCVars.MaxDownloadKb) * 1024L);
        }
        catch (Exception e)
        {
            Log.Error($"Headshot fetch from {uri.Host} threw: {e}");
            result = HeadshotResult.Fail("wg-headshot-error-download");
        }

        // Awaits resume on the game thread, so the caches are only touched there.
        if (result is { Hash: { } hash, Png: { } png })
        {
            _hashes[url] = hash;
            _images[hash] = png;
        }
        else
        {
            _fetches.Remove(url);
        }

        return result;
    }

    /// <summary>Drops finished fetches no live character uses, and their images.</summary>
    private void TrimCache()
    {
        var used = new HashSet<string>();
        var query = EntityQueryEnumerator<HeadshotComponent>();
        while (query.MoveNext(out var headshot))
        {
            used.Add(headshot.Url);
        }

        foreach (var (url, task) in _fetches.ToArray())
        {
            if (!task.IsCompleted || used.Contains(url))
                continue;

            _fetches.Remove(url);
            _hashes.Remove(url);
        }

        var keep = _hashes.Values.ToHashSet();
        foreach (var hash in _images.Keys.ToArray())
        {
            if (!keep.Contains(hash))
                _images.Remove(hash);
        }
    }

    private void OnGetVerbs(Entity<HeadshotComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor)
            || !_admin.HasAdminFlag(actor.PlayerSession, AdminFlags.Admin))
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wg-headshot-admin-remove"),
            Category = VerbCategory.Admin,
            Impact = LogImpact.Medium,
            ConfirmationPopup = true,
            Act = () =>
            {
                _adminLog.Add(LogType.Identity, LogImpact.Medium,
                    $"{ToPrettyString(user):actor} removed the headshot {ent.Comp.Url} from {ToPrettyString(ent):target}");
                RemComp<HeadshotComponent>(ent);
            },
        });
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _fetches.Clear();
        _hashes.Clear();
        _images.Clear();
        _shown.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _shown.Remove(args.Session);
        _nextPreview.Remove(args.Session);
    }
}
