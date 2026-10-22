using Content.Shared._WF.MappingMode;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._WF.MappingMode.Template;

/// <summary>
/// The template shared with the session: sending the player's own PNG to the server, receiving the one the session
/// holds, and keeping its placement in step. While a shared template is held it is the one shown and moved; its
/// opacity, show and lock stay on this client.
/// </summary>
public sealed partial class WFMappingTemplateSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>Chunks sent ahead of the last acknowledged one.</summary>
    private const int ShareInFlight = 2;

    /// <summary>A share the server says nothing about for this long is given up.</summary>
    private static readonly TimeSpan ShareStallTimeout = TimeSpan.FromSeconds(40);

    /// <summary>Placement changes go out at most this often.</summary>
    private static readonly TimeSpan TransformInterval = TimeSpan.FromSeconds(0.1);

    /// <summary>The player's PNG on its way to the server.</summary>
    private sealed class ShareUpload(byte[] bytes, int chunkCount)
    {
        public readonly byte[] Bytes = bytes;
        public readonly int ChunkCount = chunkCount;
        public int TransferId;
        public int NextChunk;
        public int Acked;
        public TimeSpan LastActivity;
    }

    private readonly WFMappingTemplateSettings _sharedSettings = new();
    private WFMappingTemplateInfo? _sharedInfo;
    private Texture? _sharedTexture;
    private int _sharedTextureId;
    private bool _sharedModified;
    private WFMappingTemplateState _lastSentState;
    private TimeSpan _nextTransformSend;
    private WFMappingUploadAssembler? _incoming;
    private ShareUpload? _upload;

    /// <summary>The session's shared template as the server last described it, or null.</summary>
    public WFMappingTemplateInfo? SharedInfo => _sharedInfo;

    /// <summary>Whether the shared template is here and shown in place of the player's own.</summary>
    public bool SharedActive => _sharedTexture != null && _sharedInfo != null && _sharedTextureId == _sharedInfo.Id;

    /// <summary>How much of the player's share has been sent, 0 to 1, or null when none is running.</summary>
    public float? SendProgress => _upload is { } upload ? (float) upload.Acked / upload.ChunkCount : null;

    /// <summary>How much of the shared template has arrived, 0 to 1, or null when nothing is arriving.</summary>
    public float? ReceiveProgress => _incoming is { } incoming && _sharedInfo?.Id == incoming.TransferId
        ? (float) incoming.Received / incoming.ChunkCount
        : null;

    /// <summary>Whether the player's own template can be shared now.</summary>
    public bool CanShare => _localBytes != null && _upload == null && _client.Session != null;

    private void InitializeShare()
    {
        SubscribeNetworkEvent<WFMappingTemplateUploadReadyEvent>(OnUploadReady);
        SubscribeNetworkEvent<WFMappingTemplateUploadAckEvent>(OnUploadAck);
        SubscribeNetworkEvent<WFMappingTemplateUploadDoneEvent>(OnUploadDone);
        SubscribeNetworkEvent<WFMappingTemplateUploadCancelledEvent>(OnUploadCancelled);
        SubscribeNetworkEvent<WFMappingTemplateDownloadChunkEvent>(OnDownloadChunk);
        SubscribeNetworkEvent<WFMappingTemplateTransformEvent>(OnTransform);
        _client.SessionUpdated += OnSessionUpdated;
    }

    private void ShutdownShare()
    {
        _client.SessionUpdated -= OnSessionUpdated;
        DropShared(true);
    }

    /// <summary>Sends pending placement changes and gives up on a share the server went quiet on.</summary>
    private void UpdateShare()
    {
        var now = _timing.RealTime;
        if (_upload != null && now - _upload.LastActivity > ShareStallTimeout)
        {
            _upload = null;
            Popup("wf-mapping-template-share-stalled");
            Changed?.Invoke();
        }

        if (!_sharedModified || now < _nextTransformSend)
            return;

        _sharedModified = false;
        if (!SharedActive || _sharedInfo is not { } info)
            return;

        var state = _sharedSettings.ToState();
        if (state == _lastSentState)
            return;

        _lastSentState = state;
        _nextTransformSend = now + TransformInterval;
        RaiseNetworkEvent(new WFMappingTemplateTransformRequest(info.Id, state));
    }

    /// <summary>Sends the player's own PNG to the session, replacing what it shares.</summary>
    public void Share()
    {
        if (_localBytes is not { } bytes || _upload != null || _client.Session == null)
            return;

        var cap = WFMappingUploadAssembler.Cap(_cfg.GetCVar(WFMappingTemplateCVars.TemplateMaxBytes));
        if (bytes.Length > cap)
        {
            Popup("wf-mapping-template-share-too-large", ("max", cap / (1024 * 1024)));
            return;
        }

        var chunkCount = WFMappingUploadAssembler.ChunksFor(bytes.Length);
        _upload = new ShareUpload(bytes, chunkCount) { LastActivity = _timing.RealTime };
        RaiseNetworkEvent(new WFMappingTemplateUploadBeginRequest(bytes.Length, chunkCount, _localSettings.ToState()));
        Changed?.Invoke();
    }

    /// <summary>Removes the shared template for everyone in the session.</summary>
    public void Unshare()
    {
        if (_sharedInfo is { } info)
            RaiseNetworkEvent(new WFMappingTemplateUnshareRequest(info.Id));
    }

    private void OnUploadReady(WFMappingTemplateUploadReadyEvent ev)
    {
        if (_upload == null || _upload.TransferId != 0)
            return;

        _upload.TransferId = ev.TransferId;
        _upload.LastActivity = _timing.RealTime;
        PumpUpload(_upload);
    }

    private void OnUploadAck(WFMappingTemplateUploadAckEvent ev)
    {
        if (_upload is not { } upload || ev.TransferId != upload.TransferId || ev.Chunk != upload.Acked)
            return;

        upload.Acked++;
        upload.LastActivity = _timing.RealTime;
        PumpUpload(upload);
        Changed?.Invoke();
    }

    private void PumpUpload(ShareUpload upload)
    {
        while (upload.NextChunk < upload.ChunkCount && upload.NextChunk - upload.Acked < ShareInFlight)
        {
            var offset = upload.NextChunk * WFMappingUploadAssembler.ChunkSize;
            var length = Math.Min(WFMappingUploadAssembler.ChunkSize, upload.Bytes.Length - offset);
            RaiseNetworkEvent(new WFMappingTemplateUploadChunkEvent(upload.TransferId, upload.NextChunk,
                upload.Bytes.AsSpan(offset, length).ToArray()));
            upload.NextChunk++;
        }
    }

    /// <summary>The server took the share; the bytes already here become the shared template.</summary>
    private void OnUploadDone(WFMappingTemplateUploadDoneEvent ev)
    {
        if (_upload is not { } upload || ev.TransferId != upload.TransferId)
            return;

        _upload = null;
        if (Decode(upload.Bytes, false) is { } texture)
        {
            _mode.CancelDrag();
            CopyLocalOptions();
            SetSharedTexture(texture, ev.TemplateId);
            ApplySharedState(ev.State);
        }

        Changed?.Invoke();
    }

    private void OnUploadCancelled(WFMappingTemplateUploadCancelledEvent ev)
    {
        if (_upload is not { } upload || ev.TransferId != 0 && ev.TransferId != upload.TransferId)
            return;

        _upload = null;
        Changed?.Invoke();
    }

    private void OnDownloadChunk(WFMappingTemplateDownloadChunkEvent ev)
    {
        // Chunk 0 always starts the transfer over, as the server does when it sends it again.
        if (ev.Chunk == 0 || _incoming == null || _incoming.TransferId != ev.TemplateId)
        {
            _incoming = null;
            if (ev.Chunk != 0 || !WFMappingUploadAssembler.TryCreate(ev.TemplateId, ev.Bytes, ev.ChunkCount,
                    _cfg.GetCVar(WFMappingTemplateCVars.TemplateMaxBytes), out _incoming))
            {
                return;
            }
        }

        if (ev.ChunkCount != _incoming.ChunkCount ||
            _incoming.Add(ev.Chunk, ev.Data) != WFMappingChunkResult.Accepted)
        {
            _incoming = null;
            return;
        }

        RaiseNetworkEvent(new WFMappingTemplateDownloadAckEvent(ev.TemplateId, ev.Chunk));
        if (_incoming.IsComplete)
        {
            var incoming = _incoming;
            _incoming = null;
            if (incoming.TryComplete(out var data) &&
                WFMappingPngHeader.Check(data, out _, out _) == WFMappingPngCheck.Ok &&
                Decode(data, false) is { } texture)
            {
                _mode.CancelDrag();
                if (_sharedTexture == null)
                    CopyLocalOptions();

                SetSharedTexture(texture, ev.TemplateId);
                if (_sharedInfo?.Id == ev.TemplateId)
                    ApplySharedState(_sharedInfo.State);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Follows the session's shared template: a new one replaces the old, none drops it.</summary>
    private void OnSessionUpdated()
    {
        var info = _client.Session?.Template;
        if (info == null)
        {
            if (_sharedInfo != null)
                DropShared(false);

            return;
        }

        var isNew = _sharedInfo?.Id != info.Id;
        _sharedInfo = info;
        if (!isNew)
            return;

        if (_sharedTextureId != info.Id)
        {
            if (SharedActive)
                _mode.CancelDrag();

            SetSharedTexture(null, 0);
        }

        if (_incoming != null && _incoming.TransferId != info.Id)
            _incoming = null;

        ApplySharedState(info.State);
        UpdateOverlay();
        Changed?.Invoke();
    }

    private void OnTransform(WFMappingTemplateTransformEvent ev)
    {
        if (_sharedInfo?.Id != ev.TemplateId)
            return;

        // A drag in progress wins; its end sends the placement anyway.
        if (SharedActive && _mode.Dragging)
            return;

        ApplySharedState(ev.State);
        Changed?.Invoke();
    }

    /// <summary>Takes a placement from the server without sending it back.</summary>
    private void ApplySharedState(WFMappingTemplateState state)
    {
        _sharedSettings.ApplyState(state);
        _lastSentState = _sharedSettings.ToState();
        _sharedModified = false;
    }

    /// <summary>A newly shown shared template starts with this client's own opacity, show and lock.</summary>
    private void CopyLocalOptions()
    {
        if (!HasLocal)
            return;

        _sharedSettings.Opacity = _localSettings.Opacity;
        _sharedSettings.Visible = _localSettings.Visible;
        _sharedSettings.Locked = _localSettings.Locked;
        _sharedSettings.Uniform = _localSettings.Uniform;
    }

    private void SetSharedTexture(Texture? texture, int id)
    {
        if (!ReferenceEquals(_sharedTexture, texture) && _sharedTexture is OwnedTexture owned)
            owned.Dispose();

        _sharedTexture = texture;
        _sharedTextureId = texture == null ? 0 : id;
        UpdateOverlay();
    }

    /// <summary>
    /// Forgets the shared template and what is arriving of it, when it is unshared; with <paramref name="leaving"/>
    /// also stops the player's own share, on leaving the session.
    /// </summary>
    private void DropShared(bool leaving)
    {
        if (SharedActive)
            _mode.CancelDrag();

        if (leaving)
        {
            if (_upload is { TransferId: > 0 } upload)
                RaiseNetworkEvent(new WFMappingTemplateUploadCancelRequest(upload.TransferId));

            _upload = null;
        }

        _incoming = null;
        _sharedInfo = null;
        _sharedModified = false;
        SetSharedTexture(null, 0);
        Changed?.Invoke();
    }
}
