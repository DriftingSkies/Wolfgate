using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared._WF.MappingMode;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map.Events;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Takes map files players upload into their own library: one transfer per player in chunks acknowledged one by
/// one, then <see cref="WFMappingUploadValidator"/>, a load onto a scratch map that strips and caps it like a
/// reference, and a library version holding the engine's save of that map, never the uploaded bytes. A file with
/// prototypes this server lacks is held, unloaded, until the player agrees to lose those entities.
/// </summary>
public sealed class WFMappingUploadSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFMappingLibrarySystem _library = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;

    /// <summary>An upload whose client sends nothing for this long is dropped.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>One player's upload: the map it goes to and the chunks so far.</summary>
    private sealed class Upload(ICommonSession player, string mapName, WFMappingUploadAssembler assembler)
    {
        public readonly ICommonSession Player = player;
        public readonly string MapName = mapName;
        public readonly WFMappingUploadAssembler Assembler = assembler;
        public TimeSpan LastActivity;
    }

    /// <summary>A complete upload with unknown prototypes, held until the player confirms or it expires.</summary>
    private sealed class Pending(ICommonSession player, string mapName, byte[] bytes,
        WFMappingUploadNeedsConfirmEvent question, TimeSpan expires)
    {
        public readonly ICommonSession Player = player;
        public readonly string MapName = mapName;
        public readonly byte[] Bytes = bytes;
        public readonly WFMappingUploadNeedsConfirmEvent Question = question;
        public readonly TimeSpan Expires = expires;
    }

    private readonly Dictionary<NetUserId, Upload> _uploads = new();
    private readonly Dictionary<NetUserId, Pending> _pending = new();

    /// <summary>Game time from which each player may start another upload.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _nextUpload = new();

    /// <summary>The Fluent id of each player's last upload outcome, for admins and tests.</summary>
    private readonly Dictionary<NetUserId, string> _lastOutcome = new();

    private readonly WFMappingThrottle<NetUserId> _requestThrottle = new(2, 2);
    private int _nextTransferId = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingUploadBeginRequest>(OnBegin);
        SubscribeNetworkEvent<WFMappingUploadChunkEvent>(OnChunk);
        SubscribeNetworkEvent<WFMappingUploadCancelRequest>(OnCancelRequest);
        SubscribeNetworkEvent<WFMappingUploadConfirmRequest>(OnConfirm);
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

        if (_uploads.Count > 0)
        {
            var now = _timing.RealTime;
            foreach (var (user, upload) in _uploads.ToList())
            {
                if (now - upload.LastActivity > StallTimeout)
                    Cancel(user, "wf-mapping-mode-upload-stalled");
            }
        }

        if (_pending.Count > 0)
        {
            var now = _timing.CurTime;
            foreach (var (user, pending) in _pending.ToList())
            {
                if (now >= pending.Expires)
                    Discard(user, "wf-mapping-mode-upload-confirm-expired");
            }
        }
    }

    /// <summary>The transfer id of a player's running upload.</summary>
    public int? ActiveTransfer(NetUserId user)
    {
        return _uploads.TryGetValue(user, out var upload) ? upload.Assembler.TransferId : null;
    }

    /// <summary>What the server asked a player whose upload waits for a confirm, or null when none waits.</summary>
    public WFMappingUploadNeedsConfirmEvent? PendingConfirmation(NetUserId user)
    {
        return _pending.TryGetValue(user, out var pending) ? pending.Question : null;
    }

    /// <summary>The Fluent id of a player's last upload outcome, success or refusal.</summary>
    public string? LastOutcome(NetUserId user)
    {
        return _lastOutcome.GetValueOrDefault(user);
    }

    private void OnBegin(WFMappingUploadBeginRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_requestThrottle.TryTake(player.UserId, _timing.CurTime))
        {
            Log.Debug($"Dropped an upload request from {player}: too many");
            return;
        }

        try
        {
            Begin(player, ev);
        }
        catch (Exception e)
        {
            Log.Error($"Starting a mapping upload for {player} failed: {e}");
            _uploads.Remove(player.UserId);
            Refuse(player, 0, "wf-mapping-mode-upload-failed");
        }
    }

    private void Begin(ICommonSession player, WFMappingUploadBeginRequest ev)
    {
        var user = player.UserId;

        // A new upload replaces one still waiting for its confirm.
        if (_pending.Remove(user, out var replaced))
            LogOutcome(replaced.Player, replaced.MapName, replaced.Bytes.Length, "replaced by a new upload");

        if (!_mapping.Enabled)
        {
            Refuse(player, 0, "wf-mapping-mode-off");
            return;
        }

        var now = _timing.CurTime;
        if (_nextUpload.TryGetValue(user, out var next) && now < next)
        {
            Refuse(player, 0, "wf-mapping-mode-upload-cooldown",
                ("seconds", (int) Math.Ceiling((next - now).TotalSeconds)));
            return;
        }

        if (_uploads.ContainsKey(user))
        {
            Refuse(player, 0, "wf-mapping-mode-upload-busy");
            return;
        }

        if (!WFMappingLibraryNames.TryNormalizeMapName(ev.Name, out var name))
        {
            Refuse(player, 0, "wf-mapping-mode-bad-name", ("max", WFMappingModeLimits.MaxNameLength));
            return;
        }

        var cap = WFMappingUploadAssembler.Cap(_cfg.GetCVar(WFMappingModeCVars.MaxUploadBytes));
        if (ev.Bytes > cap)
        {
            Refuse(player, 0, "wf-mapping-mode-upload-too-large", ("max", FormatMiB(cap)));
            return;
        }

        if (!WFMappingUploadAssembler.TryCreate(_nextTransferId++, ev.Bytes, ev.ChunkCount, cap, out var assembler))
        {
            Refuse(player, 0, "wf-mapping-mode-upload-bad-data");
            return;
        }

        if (!_library.HasRoomForMap(user, name, out var maxMaps))
        {
            Refuse(player, 0, "wf-mapping-mode-library-full-maps", ("max", maxMaps));
            return;
        }

        _uploads[user] = new Upload(player, name, assembler) { LastActivity = _timing.RealTime };
        _nextUpload[user] = now + TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(WFMappingModeCVars.UploadCooldown)));
        Log.Info($"{player} starts uploading \"{name}\": {ev.Bytes} bytes in {ev.ChunkCount} chunks");
        RaiseNetworkEvent(new WFMappingUploadReadyEvent(assembler.TransferId), player);
    }

    private void OnChunk(WFMappingUploadChunkEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_uploads.TryGetValue(user, out var upload) || ev.TransferId != upload.Assembler.TransferId)
            return;

        try
        {
            if (upload.Assembler.Add(ev.Chunk, ev.Data) != WFMappingChunkResult.Accepted)
            {
                Cancel(user, "wf-mapping-mode-upload-bad-data");
                return;
            }

            upload.LastActivity = _timing.RealTime;
            RaiseNetworkEvent(new WFMappingUploadAckEvent(ev.TransferId, ev.Chunk), upload.Player);
            if (upload.Assembler.IsComplete)
                Finish(upload);
        }
        catch (Exception e)
        {
            Log.Error($"Mapping upload from {args.SenderSession} failed: {e}");
            if (_uploads.ContainsKey(user))
                Cancel(user, "wf-mapping-mode-upload-failed");
            else
                Fail(upload.Player, upload.MapName, upload.Assembler.TransferId, upload.Assembler.Bytes,
                    "wf-mapping-mode-upload-failed");
        }
    }

    /// <summary>
    /// Validates the complete file; stores it at once when every prototype is known, or else holds it and asks the
    /// player whether to load it without the unknown ones.
    /// </summary>
    private void Finish(Upload upload)
    {
        var player = upload.Player;
        var user = player.UserId;
        var transfer = upload.Assembler.TransferId;
        _uploads.Remove(user);

        if (!upload.Assembler.TryComplete(out var bytes))
        {
            Fail(player, upload.MapName, transfer, upload.Assembler.Bytes, "wf-mapping-mode-upload-bad-data");
            return;
        }

        if (!TryValidate(player, upload.MapName, transfer, bytes, out var root, out var missing))
            return;

        if (missing.Count == 0)
        {
            Store(player, upload.MapName, transfer, bytes.Length, root, 0);
            return;
        }

        WFMappingUploadValidator.StripMissing(root, missing.ToHashSet(), out var dropped);
        var question = new WFMappingUploadNeedsConfirmEvent(transfer,
            missing.Take(WFMappingUploadNeedsConfirmEvent.MaxListed).ToList(), missing.Count, dropped);
        var wait = TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(WFMappingModeCVars.UploadConfirmSeconds)));
        _pending[user] = new Pending(player, upload.MapName, bytes, question, _timing.CurTime + wait);
        _lastOutcome[user] = "wf-mapping-mode-upload-needs-confirm";
        Log.Info($"Upload of \"{upload.MapName}\" from {player} waits for a confirm: {missing.Count} unknown " +
                 $"prototypes, {dropped} entities would go");
        RaiseNetworkEvent(question, player);
    }

    private void OnConfirm(WFMappingUploadConfirmRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        var user = player.UserId;
        if (!_pending.TryGetValue(user, out var pending) || pending.Question.TransferId != ev.TransferId)
            return;

        if (!ev.DropMissing)
        {
            Log.Debug($"Ignored a confirm without DropMissing from {player}");
            return;
        }

        if (_timing.CurTime >= pending.Expires)
        {
            Discard(user, "wf-mapping-mode-upload-confirm-expired");
            return;
        }

        _pending.Remove(user);
        try
        {
            if (!_mapping.Enabled)
            {
                Fail(player, pending.MapName, ev.TransferId, pending.Bytes.Length, "wf-mapping-mode-off");
                return;
            }

            // The file is checked again here; nothing the client says decides what goes.
            if (!TryValidate(player, pending.MapName, ev.TransferId, pending.Bytes, out var root, out var missing))
                return;

            var dropped = 0;
            if (missing.Count > 0)
                WFMappingUploadValidator.StripMissing(root, missing.ToHashSet(), out dropped);

            Log.Info($"{player} confirmed \"{pending.MapName}\" without {missing.Count} unknown prototypes: " +
                     $"{dropped} entities dropped");
            Store(player, pending.MapName, ev.TransferId, pending.Bytes.Length, root, dropped);
        }
        catch (Exception e)
        {
            Log.Error($"Confirmed mapping upload from {player} failed: {e}");
            Fail(player, pending.MapName, ev.TransferId, pending.Bytes.Length, "wf-mapping-mode-upload-failed");
        }
    }

    /// <summary>Runs the validator; on refusal ends the upload with its reason.</summary>
    private bool TryValidate(ICommonSession player, string mapName, int transfer, byte[] bytes,
        [NotNullWhen(true)] out MappingDataNode? root, out List<string> missing)
    {
        var migrations = new BeforeEntityReadEvent();
        RaiseLocalEvent(migrations);
        if (WFMappingUploadValidator.TryValidate(bytes, _prototypes, migrations.RenamedPrototypes,
                migrations.DeletedPrototypes, _cfg.GetCVar(WFMappingModeCVars.MaxEntities), out root, out missing,
                out var error, out var errorArgs))
        {
            return true;
        }

        Fail(player, mapName, transfer, bytes.Length, error, errorArgs);
        return false;
    }

    /// <summary>Loads the parsed file aside and stores the result as a library version.</summary>
    private void Store(ICommonSession player, string mapName, int transfer, int bytes, MappingDataNode root,
        int dropped)
    {
        var user = player.UserId;

        // A full load slot isn't the player's fault, so it doesn't cost them their cooldown.
        if (!_mapping.TryReserveLoad())
        {
            _nextUpload.Remove(user);
            Fail(player, mapName, transfer, bytes, "wf-mapping-mode-upload-load-busy");
            return;
        }

        if (!_mapping.TryLoadOntoScratch(root, $"upload:{user}", player, null, true, "wf-mapping-mode-upload-load-failed",
                out var load, out var refusal))
        {
            Fail(player, mapName, transfer, bytes, refusal.Key, refusal.Args);
            return;
        }

        string? version;
        string saveError;
        (string, object)[] saveArgs;
        try
        {
            _library.SaveUpload(user, mapName, load.Map, out version, out saveError, out saveArgs);
        }
        finally
        {
            if (!TerminatingOrDeleted(load.Map))
                Del(load.Map);
        }

        if (version == null)
        {
            Fail(player, mapName, transfer, bytes, saveError, saveArgs);
            return;
        }

        _lastOutcome[user] = "wf-mapping-mode-upload-done";
        Log.Info($"{player} uploaded \"{mapName}\" {version}: {bytes} bytes, {load.Grids.Count} grids, " +
                 $"{load.Entities} entities, {load.Stripped} denied entities removed, {dropped} unknown entities dropped");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} uploaded \"{mapName}\" ({bytes} bytes) to their library as {version}, {dropped} entities of unknown prototypes dropped");
        RaiseNetworkEvent(new WFMappingUploadDoneEvent(transfer, mapName, version, dropped), player);
        if (dropped > 0)
        {
            _mapping.Notify(player, true, "wf-mapping-mode-upload-done-dropped", ("name", mapName),
                ("version", version), ("dropped", dropped));
        }
        else
        {
            _mapping.Notify(player, true, "wf-mapping-mode-upload-done", ("name", mapName), ("version", version));
        }

        if (load.Stripped > 0)
            _mapping.Notify(player, true, "wf-mapping-mode-denied-stripped", ("count", load.Stripped));

        _library.SendLibrary(player);
    }

    private void OnCancelRequest(WFMappingUploadCancelRequest ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (_uploads.TryGetValue(user, out var upload) && upload.Assembler.TransferId == ev.TransferId)
            Cancel(user, "wf-mapping-mode-upload-cancelled");
        else if (_pending.TryGetValue(user, out var pending) && pending.Question.TransferId == ev.TransferId)
            Discard(user, "wf-mapping-mode-upload-cancelled");
    }

    private void OnMembershipEnded(WFMappingMembershipEndedEvent ev)
    {
        Discard(ev.User, "wf-mapping-mode-upload-cancelled");
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        var user = args.Session.UserId;
        if (_uploads.Remove(user, out var upload))
            LogOutcome(upload.Player, upload.MapName, upload.Assembler.Bytes, "the player disconnected");

        if (_pending.Remove(user, out var pending))
            LogOutcome(pending.Player, pending.MapName, pending.Bytes.Length, "the player disconnected");

        _requestThrottle.Forget(user);
        _lastOutcome.Remove(user);
    }

    /// <summary>Drops a player's running upload and tells them why.</summary>
    private void Cancel(NetUserId user, string reason, params (string, object)[] args)
    {
        if (_uploads.Remove(user, out var upload))
            Fail(upload.Player, upload.MapName, upload.Assembler.TransferId, upload.Assembler.Bytes, reason, args);
    }

    /// <summary>Drops a player's upload waiting for a confirm, bytes and all, and tells them why.</summary>
    private void Discard(NetUserId user, string reason)
    {
        if (_pending.Remove(user, out var pending))
            Fail(pending.Player, pending.MapName, pending.Question.TransferId, pending.Bytes.Length, reason);
    }

    /// <summary>Ends an upload that didn't make it: log lines, the cancelled event and the reason.</summary>
    private void Fail(ICommonSession player, string mapName, int transfer, int bytes, string reason,
        params (string, object)[] args)
    {
        LogOutcome(player, mapName, bytes, reason);
        if (player.Status != SessionStatus.Disconnected)
            Refuse(player, transfer, reason, args);
    }

    private void LogOutcome(ICommonSession player, string mapName, int bytes, string reason)
    {
        Log.Info($"Upload of \"{mapName}\" from {player} refused: {reason}");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} failed to upload \"{mapName}\" ({bytes} bytes): {reason}");
    }

    /// <summary>Tells the client its upload is over and the player why.</summary>
    private void Refuse(ICommonSession player, int transferId, string reason, params (string, object)[] args)
    {
        _lastOutcome[player.UserId] = reason;
        if (transferId == 0)
            Log.Info($"Refused an upload from {player}: {reason}");

        RaiseNetworkEvent(new WFMappingUploadCancelledEvent(transferId), player);
        _mapping.Notify(player, false, reason, args);
    }

    private static string FormatMiB(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
