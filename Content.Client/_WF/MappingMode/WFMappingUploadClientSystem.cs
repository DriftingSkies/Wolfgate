using System.IO;
using Content.Shared._WF.MappingMode;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WF.MappingMode;

/// <summary>Where the player's upload stands.</summary>
public enum WFMappingUploadStage : byte
{
    /// <summary>Asked the server, waiting for it to accept.</summary>
    Starting,
    Sending,

    /// <summary>Every chunk is in; the server checks and stores the file.</summary>
    Checking,

    /// <summary>The file uses prototypes the server lacks; it waits for the player to load it anyway or cancel.</summary>
    Confirming,
    Done,
}

/// <summary>Progress of the player's upload, for the lobby panel and the Library tab.</summary>
public sealed class WFMappingUploadProgress
{
    public string MapName = string.Empty;
    public WFMappingUploadStage Stage;
    public int Acked;
    public int ChunkCount;

    /// <summary>The library version it became, once done.</summary>
    public string Version = string.Empty;

    /// <summary>Unknown prototypes the server named while confirming, at most 25.</summary>
    public List<string> MissingPrototypes = new();
    public int MissingPrototypeCount;

    /// <summary>Entities that go if the player loads it anyway; once done, those that went.</summary>
    public int EntitiesToDrop;
}

/// <summary>
/// Uploads a map file the player picks into their library: reads it whole, sends it in chunks as the server
/// acknowledges them, and shows the progress. One upload at a time.
/// </summary>
public sealed partial class WFMappingUploadClientSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IFileDialogManager _dialogs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFMappingClientSystem _mapping = default!;

    /// <summary>Chunks sent ahead of the last acknowledged one.</summary>
    private const int InFlight = 2;

    /// <summary>An upload the server says nothing about for this long is given up.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(40);

    private bool _picking;
    private byte[] _data = Array.Empty<byte>();
    private int _transferId;
    private int _nextChunk;
    private TimeSpan _lastActivity;

    /// <summary>The running or last finished upload, or null when there is none to show.</summary>
    public WFMappingUploadProgress? Upload { get; private set; }

    /// <summary>Whether an upload is being picked or is under way.</summary>
    public bool Busy => _picking || Upload is { Stage: not WFMappingUploadStage.Done };

    public event Action? UploadUpdated;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingUploadReadyEvent>(OnReady);
        SubscribeNetworkEvent<WFMappingUploadAckEvent>(OnAck);
        SubscribeNetworkEvent<WFMappingUploadNeedsConfirmEvent>(OnNeedsConfirm);
        SubscribeNetworkEvent<WFMappingUploadDoneEvent>(OnDone);
        SubscribeNetworkEvent<WFMappingUploadCancelledEvent>(OnCancelled);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // While confirming the player takes their time; the server ends it if they take too long.
        if (Upload is { Stage: not (WFMappingUploadStage.Done or WFMappingUploadStage.Confirming) } &&
            _timing.RealTime - _lastActivity > StallTimeout)
        {
            Drop();
            _mapping.ReportLocal("wf-mapping-mode-upload-stalled", false);
        }
    }

    /// <summary>Opens the file dialog and uploads the picked map file.</summary>
    public async void PickAndUpload()
    {
        if (Busy)
        {
            _mapping.ReportLocal("wf-mapping-mode-upload-busy", false);
            return;
        }

        _picking = true;
        try
        {
            var filters = new FileDialogFilters(new FileDialogFilters.Group("yml"));
            var (stream, fileName) = await _dialogs.GetFileAndName(filters, FileAccess.Read);
            if (stream == null)
                return;

            byte[]? data;
            var cap = WFMappingUploadAssembler.Cap(_cfg.GetCVar(WFMappingModeCVars.MaxUploadBytes));
            await using (stream)
            {
                data = ReadCapped(stream, cap);
            }

            if (data == null)
            {
                _mapping.ReportLocal("wf-mapping-mode-upload-too-large", false,
                    ("max", WFMappingLibraryList.FormatMiB(cap)));
                return;
            }

            if (data.Length == 0)
            {
                _mapping.ReportLocal("wf-mapping-mode-upload-empty-file", false);
                return;
            }

            Start(WFMappingLibraryNames.UploadNameFromFile(fileName), data);
        }
        catch (Exception e)
        {
            Log.Error($"Reading a map file to upload failed: {e}");
            _mapping.ReportLocal("wf-mapping-mode-upload-read-failed", false);
        }
        finally
        {
            _picking = false;
        }
    }

    /// <summary>Reads a stream whole; null when it is longer than <paramref name="cap"/> bytes.</summary>
    private static byte[]? ReadCapped(Stream stream, int cap)
    {
        if (stream.CanSeek && stream.Length > cap)
            return null;

        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > cap)
                return null;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    /// <summary>Asks the server to take a file under a map name.</summary>
    public void Start(string mapName, byte[] data)
    {
        // Not Busy: the picker that hands the file over is still open when this runs.
        if (Upload is { Stage: not WFMappingUploadStage.Done })
        {
            _mapping.ReportLocal("wf-mapping-mode-upload-busy", false);
            return;
        }

        _data = data;
        _transferId = 0;
        _nextChunk = 0;
        _lastActivity = _timing.RealTime;
        Upload = new WFMappingUploadProgress
        {
            MapName = mapName,
            Stage = WFMappingUploadStage.Starting,
            ChunkCount = WFMappingUploadAssembler.ChunksFor(data.Length),
        };

        RaiseNetworkEvent(new WFMappingUploadBeginRequest(mapName, data.Length, Upload.ChunkCount));
        UploadUpdated?.Invoke();
    }

    /// <summary>Stops the running upload.</summary>
    public void Cancel()
    {
        if (Upload is not { Stage: not WFMappingUploadStage.Done })
            return;

        RaiseNetworkEvent(new WFMappingUploadCancelRequest(_transferId));
        Drop();
        _mapping.ReportLocal("wf-mapping-mode-upload-cancelled", false);
    }

    /// <summary>Tells the server to store the held upload without the entities it can't load.</summary>
    public void ConfirmDropMissing()
    {
        if (Upload is not { Stage: WFMappingUploadStage.Confirming } progress)
            return;

        progress.Stage = WFMappingUploadStage.Checking;
        _lastActivity = _timing.RealTime;
        RaiseNetworkEvent(new WFMappingUploadConfirmRequest(_transferId, true));
        UploadUpdated?.Invoke();
    }

    private void OnReady(WFMappingUploadReadyEvent ev)
    {
        if (Upload is not { Stage: WFMappingUploadStage.Starting } progress)
            return;

        _transferId = ev.TransferId;
        progress.Stage = WFMappingUploadStage.Sending;
        Pump();
        UploadUpdated?.Invoke();
    }

    private void OnAck(WFMappingUploadAckEvent ev)
    {
        if (Upload is not { Stage: WFMappingUploadStage.Sending } progress ||
            ev.TransferId != _transferId ||
            ev.Chunk != progress.Acked)
        {
            return;
        }

        progress.Acked++;
        if (progress.Acked >= progress.ChunkCount)
        {
            progress.Stage = WFMappingUploadStage.Checking;
            _data = Array.Empty<byte>();
            _lastActivity = _timing.RealTime;
        }
        else
        {
            Pump();
        }

        UploadUpdated?.Invoke();
    }

    /// <summary>Sends chunks until <see cref="InFlight"/> are unacknowledged or the file is all sent.</summary>
    private void Pump()
    {
        if (Upload is not { } progress)
            return;

        while (_nextChunk < progress.ChunkCount && _nextChunk - progress.Acked < InFlight)
        {
            var offset = _nextChunk * WFMappingUploadAssembler.ChunkSize;
            var length = Math.Min(WFMappingUploadAssembler.ChunkSize, _data.Length - offset);
            RaiseNetworkEvent(new WFMappingUploadChunkEvent(_transferId, _nextChunk, _data.AsSpan(offset, length).ToArray()));
            _nextChunk++;
        }

        _lastActivity = _timing.RealTime;
    }

    private void OnNeedsConfirm(WFMappingUploadNeedsConfirmEvent ev)
    {
        if (Upload is not { Stage: WFMappingUploadStage.Checking } progress || ev.TransferId != _transferId)
            return;

        progress.Stage = WFMappingUploadStage.Confirming;
        progress.MissingPrototypes = ev.MissingPrototypes;
        progress.MissingPrototypeCount = ev.MissingPrototypeCount;
        progress.EntitiesToDrop = ev.EntitiesToDrop;
        UploadUpdated?.Invoke();
    }

    private void OnDone(WFMappingUploadDoneEvent ev)
    {
        if (Upload is not { } progress || ev.TransferId != _transferId)
            return;

        progress.Stage = WFMappingUploadStage.Done;
        progress.MapName = ev.MapName;
        progress.Version = ev.Version;
        progress.EntitiesToDrop = ev.Dropped;
        _data = Array.Empty<byte>();
        UploadUpdated?.Invoke();
    }

    private void OnCancelled(WFMappingUploadCancelledEvent ev)
    {
        // A refused begin carries no transfer id; it only ends an upload still waiting to start.
        if (Upload is not { Stage: not WFMappingUploadStage.Done } progress ||
            ev.TransferId != _transferId ||
            ev.TransferId == 0 && progress.Stage != WFMappingUploadStage.Starting)
        {
            return;
        }

        Drop();
    }

    private void Drop()
    {
        _data = Array.Empty<byte>();
        _transferId = 0;
        Upload = null;
        UploadUpdated?.Invoke();
    }
}
