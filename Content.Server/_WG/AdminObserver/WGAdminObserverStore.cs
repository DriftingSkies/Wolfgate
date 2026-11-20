using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Administration;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._WG.AdminObserver;

/// <summary>
/// Keeps each connected admin's <see cref="WGAdminObserverData"/> in memory: loaded when they join or gain the Admin
/// flag, sent to their client, and written back shortly after each change and when they leave or lose the flag.
/// </summary>
public sealed partial class WGAdminObserverStore : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>How long after the last change a document is written.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    /// <summary>How long after a failed write it is tried again.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Raised when an admin's document has come into memory.</summary>
    public event Action<ICommonSession>? Loaded;

    private readonly Dictionary<NetUserId, WGAdminObserverData> _data = new();
    private readonly Dictionary<NetUserId, CancellationTokenSource> _pending = new();
    private readonly HashSet<NetUserId> _loading = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGAdminObserverRequestDataEvent>(OnRequest);
        _players.PlayerStatusChanged += OnStatusChanged;
        _admin.OnPermsChanged += OnPermsChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnStatusChanged;
        _admin.OnPermsChanged -= OnPermsChanged;

        foreach (var user in new List<NetUserId>(_pending.Keys))
        {
            _ = FlushAsync(user);
        }
    }

    /// <summary>Whether a player may use the admin observer: the Admin flag, as for <c>aghost</c>.</summary>
    public bool IsAllowed(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Admin);
    }

    /// <summary>Whether a player's document is in memory.</summary>
    public bool IsLoaded(NetUserId user)
    {
        return _data.ContainsKey(user);
    }

    /// <summary>An admin's document, or null when they are not allowed or it has not loaded yet.</summary>
    public WGAdminObserverData? Get(ICommonSession session)
    {
        return IsAllowed(session) && _data.TryGetValue(session.UserId, out var data) ? data : null;
    }

    /// <summary>
    /// Changes an admin's document, then saves it and sends it to their client. False, with nothing changed, when
    /// they are not allowed, it has not loaded, or the result would be too large.
    /// </summary>
    public bool Change(ICommonSession session, Action<WGAdminObserverData> change)
    {
        return IsAllowed(session) && ChangeLoaded(session.UserId, change);
    }

    /// <summary>
    /// <see cref="Change"/> for a document that is in memory, without asking for the flag again. For what an
    /// admin's own observer saves as they leave it, when their session may already be closing.
    /// </summary>
    public bool ChangeLoaded(NetUserId user, Action<WGAdminObserverData> change)
    {
        if (!_data.TryGetValue(user, out var data))
            return false;

        WGAdminObserverSerializer.TrySerialize(data, out var before);
        change(data);
        Sanitize(data);

        if (!WGAdminObserverSerializer.TrySerialize(data, out _))
        {
            _data[user] = WGAdminObserverSerializer.Deserialize(before) ?? new WGAdminObserverData();
            return false;
        }

        ScheduleSave(user);
        if (_players.TryGetSessionById(user, out var session) && session.Status != SessionStatus.Disconnected)
            Send(session);

        return true;
    }

    /// <summary>Writes a document now if a save is waiting. False when the write failed; it is then tried again later.</summary>
    public Task<bool> FlushAsync(NetUserId user)
    {
        if (!_pending.Remove(user, out var cancel))
            return Task.FromResult(true);

        cancel.Cancel();
        return SaveAsync(user);
    }

    /// <summary>Loads an admin's document if it is not in memory yet, then sends it to their client.</summary>
    public async Task EnsureLoadedAsync(ICommonSession session)
    {
        var user = session.UserId;
        if (!IsAllowed(session))
            return;

        if (_data.ContainsKey(user))
        {
            Send(session);
            return;
        }

        if (!_loading.Add(user))
            return;

        string? json = null;
        var failed = false;
        try
        {
            json = await _db.GetAdminObserverDataAsync(user.UserId);
        }
        catch (Exception e)
        {
            Log.Error($"Could not load the admin observer document of {session.Name}: {e}");
            failed = true;
        }
        finally
        {
            _loading.Remove(user);
        }

        // A failed read stays unloaded, so a new document never overwrites the saved one; the next request reads again.
        if (failed || session.Status == SessionStatus.Disconnected || _data.ContainsKey(user))
            return;

        var data = json == null ? null : WGAdminObserverSerializer.Deserialize(json);
        if (json != null && data == null)
            Log.Warning($"The admin observer document of {session.Name} could not be read; starting a new one.");

        _data[user] = Sanitize(data ?? Stock());
        Send(session);
        Loaded?.Invoke(session);
    }

    /// <summary>A new admin's document: the plain ghost, and their selected character in what it would spawn in.</summary>
    private WGAdminObserverData Stock()
    {
        var data = new WGAdminObserverData();
        data.Presets.Add(new WGAvatarPreset { Name = Loc.GetString("wg-observer-preset-stock-ghost") });
        data.Presets.Add(new WGAvatarPreset
        {
            Name = Loc.GetString("wg-observer-preset-stock-own"),
            Kind = WGAvatarKind.Character,
        });
        return data;
    }

    private WGAdminObserverData Sanitize(WGAdminObserverData data)
    {
        return WGAdminObserverSerializer.Sanitize(
            data,
            id => _prototypes.TryIndex<EntityPrototype>(id, out var proto) && !proto.Abstract,
            id => _prototypes.HasIndex<StartingGearPrototype>(id));
    }

    private void Send(ICommonSession session)
    {
        RaiseNetworkEvent(new WGAdminObserverDataEvent { Data = Get(session) }, session);
    }

    private void ScheduleSave(NetUserId user, TimeSpan? delay = null)
    {
        if (_pending.Remove(user, out var old))
            old.Cancel();

        var cancel = new CancellationTokenSource();
        _pending[user] = cancel;
        Timer.Spawn(delay ?? SaveDelay, () =>
        {
            if (_pending.TryGetValue(user, out var current) && current == cancel)
                _ = SettleAsync(user);
        }, cancel.Token);
    }

    /// <summary>Writes a document. False when the write failed; it is then pending again, unless a newer save is.</summary>
    private async Task<bool> SaveAsync(NetUserId user)
    {
        if (!_data.TryGetValue(user, out var data) || !WGAdminObserverSerializer.TrySerialize(data, out var json))
            return true;

        try
        {
            await _db.SaveAdminObserverDataAsync(user.UserId, json);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Could not save the admin observer document of {user}: {e}");
            if (!_pending.ContainsKey(user) && _data.ContainsKey(user))
                ScheduleSave(user, RetryDelay);

            return false;
        }
    }

    private void OnRequest(WGAdminObserverRequestDataEvent ev, EntitySessionEventArgs args)
    {
        _ = EnsureLoadedAsync(args.SenderSession);
    }

    private void OnStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        switch (args.NewStatus)
        {
            case SessionStatus.InGame:
                _ = EnsureLoadedAsync(args.Session);
                break;
            case SessionStatus.Disconnected:
                // A frame later, so the observer the player is leaving can still save its kit.
                var user = args.Session.UserId;
                Timer.Spawn(TimeSpan.Zero, () => _ = SettleAsync(user));
                break;
        }
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (IsAllowed(args.Player))
        {
            _ = EnsureLoadedAsync(args.Player);
            return;
        }

        RaiseNetworkEvent(new WGAdminObserverDataEvent(), args.Player);
        _ = SettleAsync(args.Player.UserId);
    }

    /// <summary>
    /// Writes a waiting save, then forgets the document if its admin has left or lost the flag. A document whose
    /// write failed, or that changed meanwhile, stays until a later save succeeds.
    /// </summary>
    private async Task SettleAsync(NetUserId user)
    {
        if (!await FlushAsync(user) || _pending.ContainsKey(user))
            return;

        if (!_players.TryGetSessionById(user, out var session)
            || session.Status == SessionStatus.Disconnected
            || !IsAllowed(session))
        {
            _data.Remove(user);
        }
    }
}
