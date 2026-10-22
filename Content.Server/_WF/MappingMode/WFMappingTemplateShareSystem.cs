using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Content.Server._WF.MappingTools;
using Content.Server.Administration.Logs;
using Content.Shared._WF.MappingMode;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingMode;

/// <summary>A session's shared PNG template: the bytes, never decoded, and where it sits.</summary>
public sealed class WFMappingSessionTemplate(int id, byte[] png, int width, int height, NetUserId sharedBy,
    string sharedByName, WFMappingTemplateState state)
{
    public readonly int Id = id;
    public readonly byte[] Png = png;
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly NetUserId SharedBy = sharedBy;
    public readonly string SharedByName = sharedByName;
    public WFMappingTemplateState State = state;

    /// <summary>Members whose client holds it, with the observer they had when it arrived.</summary>
    public readonly Dictionary<NetUserId, EntityUid> Delivered = new();

    public WFMappingTemplateInfo Info()
    {
        return new WFMappingTemplateInfo(Id, SharedBy, SharedByName, Width, Height, Png.Length, State);
    }
}

/// <summary>
/// Templates shared with a whole session: a member uploads the PNG they hold in acknowledged chunks, the server checks
/// its signature and size without decoding it, keeps it on the session and streams it to every present member,
/// including those who join later. Its placement is session state any member may change; opacity, show and lock stay
/// on each client. Saved with the map as <c>template.png</c> and <c>template.yml</c> in the version folder.
/// </summary>
public sealed partial class WFMappingTemplateShareSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;

    /// <summary>The shared image in a library version folder.</summary>
    public const string ImageFile = "template.png";

    /// <summary>The shared placement in a library version folder.</summary>
    public const string SettingsFile = "template.yml";

    /// <summary>Chunks sent ahead of the last acknowledged one.</summary>
    private const int InFlight = 2;

    /// <summary>A transfer whose other end sends nothing for this long is dropped.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often present members are checked for a template they lack.</summary>
    private static readonly TimeSpan DeliveryCheckInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>One player's share in progress, bound to the session they were in when it began.</summary>
    private sealed class Upload(ICommonSession player, int sessionId, WFMappingUploadAssembler assembler,
        WFMappingTemplateState state)
    {
        public readonly ICommonSession Player = player;
        public readonly int SessionId = sessionId;
        public readonly WFMappingUploadAssembler Assembler = assembler;
        public readonly WFMappingTemplateState State = state;
        public TimeSpan LastActivity;
    }

    /// <summary>The shared template on its way to one member's observer.</summary>
    private sealed class Delivery(ICommonSession player, int sessionId, WFMappingSessionTemplate template,
        EntityUid observer, int chunkCount)
    {
        public readonly ICommonSession Player = player;
        public readonly int SessionId = sessionId;
        public readonly WFMappingSessionTemplate Template = template;
        public readonly EntityUid Observer = observer;
        public readonly int ChunkCount = chunkCount;
        public int NextChunk;
        public int Acked;
        public TimeSpan LastActivity;
    }

    private readonly Dictionary<NetUserId, Upload> _uploads = new();
    private readonly Dictionary<NetUserId, Delivery> _deliveries = new();

    /// <summary>Game time from which each player may start another share.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _nextShare = new();

    /// <summary>The Fluent id of each player's last share outcome, for tests.</summary>
    private readonly Dictionary<NetUserId, string> _lastOutcome = new();

    private readonly WFMappingThrottle<NetUserId> _requestThrottle = new(2, 4);

    /// <summary>Above the client's 10 a second, so a well-behaved drag never loses its last step.</summary>
    private readonly WFMappingThrottle<NetUserId> _transformThrottle = new(15, 15);

    private int _nextTransferId = 1;
    private int _nextTemplateId = 1;
    private TimeSpan _nextDeliveryCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingTemplateUploadBeginRequest>(OnBegin);
        SubscribeNetworkEvent<WFMappingTemplateUploadChunkEvent>(OnChunk);
        SubscribeNetworkEvent<WFMappingTemplateUploadCancelRequest>(OnCancelRequest);
        SubscribeNetworkEvent<WFMappingTemplateDownloadAckEvent>(OnDownloadAck);
        SubscribeNetworkEvent<WFMappingTemplateTransformRequest>(OnTransform);
        SubscribeNetworkEvent<WFMappingTemplateUnshareRequest>(OnUnshare);
        SubscribeLocalEvent<WFMappingMembershipEndedEvent>(OnMembershipEnded);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.RealTime;
        if (_uploads.Count > 0)
        {
            foreach (var (user, upload) in _uploads.ToList())
            {
                if (now - upload.LastActivity > StallTimeout)
                    Cancel(user, "wf-mapping-template-share-stalled");
            }
        }

        if (_deliveries.Count > 0)
        {
            foreach (var (user, delivery) in _deliveries.ToList())
            {
                if (now - delivery.LastActivity > StallTimeout || !StillWanted(user, delivery))
                    _deliveries.Remove(user);
            }
        }

        var gameNow = _timing.CurTime;
        if (gameNow < _nextDeliveryCheck)
            return;

        _nextDeliveryCheck = gameNow + DeliveryCheckInterval;
        foreach (var session in _mapping.Sessions)
        {
            if (session.Template is { } template)
                Deliver(session, template);
        }
    }

    /// <summary>The transfer id of a player's running share.</summary>
    public int? ActiveUpload(NetUserId user)
    {
        return _uploads.TryGetValue(user, out var upload) ? upload.Assembler.TransferId : null;
    }

    /// <summary>The id of the shared template being sent to a player.</summary>
    public int? ActiveDelivery(NetUserId user)
    {
        return _deliveries.TryGetValue(user, out var delivery) ? delivery.Template.Id : null;
    }

    /// <summary>The Fluent id of a player's last share outcome, success or refusal.</summary>
    public string? LastOutcome(NetUserId user)
    {
        return _lastOutcome.GetValueOrDefault(user);
    }

    /// <summary>The share cap in force.</summary>
    public int MaxBytes => WFMappingUploadAssembler.Cap(_cfg.GetCVar(WFMappingTemplateCVars.TemplateMaxBytes));

    /// <summary>A new shared template with a fresh id, its placement clamped to depths of the given range.</summary>
    public WFMappingSessionTemplate Create(byte[] png, int width, int height, NetUserId sharedBy, string sharedByName,
        WFMappingTemplateState state, int minDepth, int maxDepth)
    {
        return new WFMappingSessionTemplate(_nextTemplateId++, png, width, height, sharedBy, sharedByName,
            WFMappingTemplateSettings.Clamp(state, minDepth, maxDepth));
    }

    #region Sharing

    private void OnBegin(WFMappingTemplateUploadBeginRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_requestThrottle.TryTake(player.UserId, _timing.CurTime))
        {
            Log.Debug($"Dropped a template share request from {player}: too many");
            return;
        }

        var user = player.UserId;
        if (!_mapping.Enabled || !_mapping.TryGetPresent(player, out var session))
        {
            Refuse(player, 0, "wf-mapping-template-share-not-present");
            return;
        }

        var now = _timing.CurTime;
        if (_nextShare.TryGetValue(user, out var next) && now < next)
        {
            Refuse(player, 0, "wf-mapping-template-share-cooldown",
                ("seconds", (int) Math.Ceiling((next - now).TotalSeconds)));
            return;
        }

        if (_uploads.ContainsKey(user))
        {
            Refuse(player, 0, "wf-mapping-template-share-busy");
            return;
        }

        var cap = MaxBytes;
        if (ev.Bytes > cap)
        {
            Refuse(player, 0, "wf-mapping-template-share-too-large", ("max", FormatMiB(cap)));
            return;
        }

        if (!WFMappingUploadAssembler.TryCreate(_nextTransferId++, ev.Bytes, ev.ChunkCount, cap, out var assembler))
        {
            Refuse(player, 0, "wf-mapping-template-share-bad-data");
            return;
        }

        _uploads[user] = new Upload(player, session.Id, assembler, ev.State) { LastActivity = _timing.RealTime };
        _nextShare[user] = now + TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(WFMappingTemplateCVars.TemplateCooldown)));
        Log.Info($"{player} starts sharing a template with mapping session {session.Code}: {ev.Bytes} bytes");
        RaiseNetworkEvent(new WFMappingTemplateUploadReadyEvent(assembler.TransferId), player);
    }

    private void OnChunk(WFMappingTemplateUploadChunkEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_uploads.TryGetValue(user, out var upload) || ev.TransferId != upload.Assembler.TransferId)
            return;

        if (upload.Assembler.Add(ev.Chunk, ev.Data) != WFMappingChunkResult.Accepted)
        {
            Cancel(user, "wf-mapping-template-share-bad-data");
            return;
        }

        upload.LastActivity = _timing.RealTime;
        RaiseNetworkEvent(new WFMappingTemplateUploadAckEvent(ev.TransferId, ev.Chunk), upload.Player);
        if (!upload.Assembler.IsComplete)
            return;

        _uploads.Remove(user);
        try
        {
            Finish(upload);
        }
        catch (Exception e)
        {
            Log.Error($"Template share from {upload.Player} failed: {e}");
            Refuse(upload.Player, upload.Assembler.TransferId, "wf-mapping-template-share-failed");
        }
    }

    /// <summary>Checks the complete PNG and makes it the session's shared template.</summary>
    private void Finish(Upload upload)
    {
        var player = upload.Player;
        var transfer = upload.Assembler.TransferId;
        if (!upload.Assembler.TryComplete(out var png))
        {
            Refuse(player, transfer, "wf-mapping-template-share-bad-data");
            return;
        }

        switch (WFMappingPngHeader.Check(png, MaxBytes, out var width, out var height))
        {
            case WFMappingPngCheck.Ok:
                break;
            case WFMappingPngCheck.FileTooLarge:
                Refuse(player, transfer, "wf-mapping-template-share-too-large", ("max", FormatMiB(MaxBytes)));
                return;
            case WFMappingPngCheck.ImageTooLarge:
                Refuse(player, transfer, "wf-mapping-template-image-too-large", ("side", WFMappingPngHeader.MaxSide));
                return;
            default:
                Refuse(player, transfer, "wf-mapping-template-not-png");
                return;
        }

        if (!_mapping.TryGetPresent(player, out var session) || session.Id != upload.SessionId)
        {
            Refuse(player, transfer, "wf-mapping-template-share-not-present");
            return;
        }

        DepthRange(session, out var minDepth, out var maxDepth);
        var template = Create(png, width, height, player.UserId, player.Name, upload.State, minDepth, maxDepth);
        if (session.Present.TryGetValue(player.UserId, out var observer))
            template.Delivered[player.UserId] = observer;

        if (session.Template is { } old)
            DropDeliveries(old);

        session.Template = template;
        _lastOutcome[player.UserId] = "wf-mapping-template-share-done";
        Log.Info($"{player} shared a {width}x{height} template ({png.Length} bytes) with mapping session {session.Code}");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} shared a {width}x{height} template ({png.Length} bytes) with mapping session {session.Code}");

        RaiseNetworkEvent(new WFMappingTemplateUploadDoneEvent(transfer, template.Id, template.State), player);
        Changed(session);
        _mapping.Notify(player, true, "wf-mapping-template-share-done");
        NotifyOthers(session, player.UserId, "wf-mapping-template-share-received", ("name", player.Name));
        Deliver(session, template);
    }

    private void OnCancelRequest(WFMappingTemplateUploadCancelRequest ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (_uploads.TryGetValue(user, out var upload) && upload.Assembler.TransferId == ev.TransferId)
            Cancel(user, "wf-mapping-template-share-cancelled");
    }

    private void OnUnshare(WFMappingTemplateUnshareRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_requestThrottle.TryTake(player.UserId, _timing.CurTime) ||
            !_mapping.TryGetPresent(player, out var session) || session.Template is not { } template ||
            template.Id != ev.TemplateId)
        {
            return;
        }

        session.Template = null;
        DropDeliveries(template);
        Log.Info($"{player} removed the shared template of mapping session {session.Code}");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} removed the shared template of mapping session {session.Code}");
        Changed(session);
        _mapping.Notify(player, true, "wf-mapping-template-unshare-done");
        NotifyOthers(session, player.UserId, "wf-mapping-template-unshared", ("name", player.Name));
    }

    private void OnTransform(WFMappingTemplateTransformRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_mapping.TryGetPresent(player, out var session) ||
            session.Template is not { } template || template.Id != ev.TemplateId)
        {
            return;
        }

        if (!_transformThrottle.TryTake(player.UserId, _timing.CurTime))
        {
            Log.Debug($"Dropped a template move from {player}: too many");
            return;
        }

        DepthRange(session, out var minDepth, out var maxDepth);
        var state = WFMappingTemplateSettings.Clamp(ev.State, minDepth, maxDepth);
        if (state == template.State)
        {
            if (state != ev.State)
                RaiseNetworkEvent(new WFMappingTemplateTransformEvent(template.Id, state), player);

            return;
        }

        template.State = state;
        var message = new WFMappingTemplateTransformEvent(template.Id, state);
        foreach (var user in session.Present.Keys)
        {
            // The sender already shows what it asked for, unless the clamp changed it.
            if (user == player.UserId && state == ev.State)
                continue;

            if (_players.TryGetSessionById(user, out var member))
                RaiseNetworkEvent(message, member);
        }

        MarkDirty(session);
    }

    #endregion

    #region Delivery

    /// <summary>Starts sending the template to every present member whose client lacks it.</summary>
    private void Deliver(WFMappingSession session, WFMappingSessionTemplate template)
    {
        foreach (var (user, observer) in session.Present)
        {
            if (template.Delivered.TryGetValue(user, out var had) && had == observer)
                continue;

            if (_deliveries.TryGetValue(user, out var running) && running.Template == template &&
                running.Observer == observer)
            {
                continue;
            }

            if (!_players.TryGetSessionById(user, out var player))
                continue;

            var delivery = new Delivery(player, session.Id, template, observer,
                WFMappingUploadAssembler.ChunksFor(template.Png.Length))
            {
                LastActivity = _timing.RealTime,
            };

            _deliveries[user] = delivery;
            Pump(delivery);
        }
    }

    /// <summary>Sends chunks until <see cref="InFlight"/> are unacknowledged or the PNG is all sent.</summary>
    private void Pump(Delivery delivery)
    {
        var png = delivery.Template.Png;
        while (delivery.NextChunk < delivery.ChunkCount && delivery.NextChunk - delivery.Acked < InFlight)
        {
            var offset = delivery.NextChunk * WFMappingUploadAssembler.ChunkSize;
            var length = Math.Min(WFMappingUploadAssembler.ChunkSize, png.Length - offset);
            RaiseNetworkEvent(new WFMappingTemplateDownloadChunkEvent(delivery.Template.Id, png.Length,
                delivery.ChunkCount, delivery.NextChunk, png.AsSpan(offset, length).ToArray()), delivery.Player);
            delivery.NextChunk++;
        }

        delivery.LastActivity = _timing.RealTime;
    }

    private void OnDownloadAck(WFMappingTemplateDownloadAckEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_deliveries.TryGetValue(user, out var delivery) ||
            ev.TemplateId != delivery.Template.Id ||
            ev.Chunk != delivery.Acked ||
            ev.Chunk >= delivery.NextChunk)
        {
            return;
        }

        delivery.Acked++;
        if (delivery.Acked < delivery.ChunkCount)
        {
            Pump(delivery);
            return;
        }

        _deliveries.Remove(user);
        if (StillWanted(user, delivery))
            delivery.Template.Delivered[user] = delivery.Observer;
    }

    /// <summary>Stops sending a template that is no longer shared.</summary>
    private void DropDeliveries(WFMappingSessionTemplate template)
    {
        foreach (var (user, delivery) in _deliveries.ToList())
        {
            if (delivery.Template == template)
                _deliveries.Remove(user);
        }
    }

    /// <summary>Whether a delivery's member is still present on the same observer and the template still shared.</summary>
    private bool StillWanted(NetUserId user, Delivery delivery)
    {
        return _mapping.TryGetSession(delivery.SessionId, out var session) &&
               session.Template == delivery.Template &&
               session.Present.TryGetValue(user, out var observer) &&
               observer == delivery.Observer;
    }

    #endregion

    #region Library

    /// <summary>Writes a shared template into a library version folder; throws when a write fails.</summary>
    public void WriteSaved(WFMappingSessionTemplate template, ResPath folder)
    {
        var settings = new WFMappingTemplateSettings();
        settings.ApplyState(template.State);
        _res.UserData.WriteAllBytes(folder / ImageFile, template.Png);
        _res.UserData.WriteAllText(folder / SettingsFile, settings.Write());
    }

    /// <summary>
    /// Reads the shared template saved in a version folder, as shared by <paramref name="player"/>; null when there
    /// is none, or it is unreadable or over the caps.
    /// </summary>
    public WFMappingSessionTemplate? ReadSaved(ResPath image, ResPath? placement, ICommonSession player, int minDepth,
        int maxDepth)
    {
        try
        {
            byte[] png;
            using (var stream = _res.UserData.OpenRead(image))
            {
                if (stream.Length > MaxBytes)
                {
                    Log.Warning($"Saved mapping template {image} is over the share cap; not restoring it");
                    return null;
                }

                png = new byte[stream.Length];
                stream.ReadExactly(png);
            }

            if (WFMappingPngHeader.Check(png, MaxBytes, out var width, out var height) != WFMappingPngCheck.Ok)
            {
                Log.Warning($"Saved mapping template {image} isn't a PNG within the caps; not restoring it");
                return null;
            }

            var settings = new WFMappingTemplateSettings();
            if (placement is { } path && _res.UserData.TryReadAllText(path, out var text))
                settings = WFMappingTemplateSettings.Read(text);

            return Create(png, width, height, player.UserId, player.Name, settings.ToState(), minDepth, maxDepth);
        }
        catch (Exception e)
        {
            Log.Warning($"Reading the saved mapping template {image} failed: {e.Message}");
            return null;
        }
    }

    #endregion

    private void OnMembershipEnded(WFMappingMembershipEndedEvent ev)
    {
        _deliveries.Remove(ev.User);
        Cancel(ev.User, "wf-mapping-template-share-cancelled");
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        var user = args.Session.UserId;
        if (_uploads.Remove(user))
            Log.Info($"Template share from {args.Session} dropped: the player disconnected");

        _deliveries.Remove(user);
        _requestThrottle.Forget(user);
        _transformThrottle.Forget(user);
        _lastOutcome.Remove(user);
    }

    /// <summary>The depths of a session's levels, for clamping a template's z-level.</summary>
    private void DepthRange(WFMappingSession session, out int minDepth, out int maxDepth)
    {
        var levels = _zLevels.GetLevels(session.RootMap, out var ground);
        minDepth = -ground;
        maxDepth = Math.Max(minDepth, levels.Count - 1 - ground);
    }

    /// <summary>Marks the session unsaved and sends every member its new state.</summary>
    private void Changed(WFMappingSession session)
    {
        session.Dirty = true;
        _mapping.SendState(session);
    }

    /// <summary>Marks the session unsaved, sending its state only when that is news.</summary>
    private void MarkDirty(WFMappingSession session)
    {
        if (session.Dirty)
            return;

        session.Dirty = true;
        _mapping.SendState(session);
    }

    private void NotifyOthers(WFMappingSession session, NetUserId except, string key, params (string, object)[] args)
    {
        foreach (var user in session.Present.Keys)
        {
            if (user != except && _players.TryGetSessionById(user, out var member))
                _mapping.Notify(member, true, key, args);
        }
    }

    /// <summary>Drops a player's running share and tells them why.</summary>
    private void Cancel(NetUserId user, string reason, params (string, object)[] args)
    {
        if (!_uploads.Remove(user, out var upload))
            return;

        Log.Info($"Template share from {upload.Player} stopped: {reason}");
        if (upload.Player.Status != SessionStatus.Disconnected)
            Refuse(upload.Player, upload.Assembler.TransferId, reason, args);
    }

    /// <summary>Tells the client its share is over and the player why.</summary>
    private void Refuse(ICommonSession player, int transferId, string reason, params (string, object)[] args)
    {
        _lastOutcome[player.UserId] = reason;
        if (transferId == 0)
            Log.Info($"Refused a template share from {player}: {reason}");

        RaiseNetworkEvent(new WFMappingTemplateUploadCancelledEvent(transferId), player);
        _mapping.Notify(player, false, reason, args);
    }

    private static string FormatMiB(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
