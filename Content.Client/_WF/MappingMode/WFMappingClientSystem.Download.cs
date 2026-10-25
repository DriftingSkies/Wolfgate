using System.IO;
using System.Linq;
using Content.Shared._WF.MappingMode;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode;

/// <summary>Progress of the player's download, for the lobby panel and the Library tab.</summary>
public sealed class WFMappingDownloadProgress
{
    public string MapName = string.Empty;
    public string Version = string.Empty;
    public int FileIndex;
    public int FileCount;
    public int Chunks;
    public int ChunkCount;

    /// <summary>Still receiving; false once every file is written, or held for the save dialog.</summary>
    public bool Active;

    /// <summary>The files go through the save dialog, one at a time, instead of into the data folder.</summary>
    public bool SaveTo;

    /// <summary>A ship download: the grid file, then the vessel prototype.</summary>
    public bool Ship;

    /// <summary>An export from the wizard: one zip laid out like the repository, or the bare grid file of a grid export.</summary>
    public bool Zip;

    /// <summary>The file the save dialog is open for, while it is.</summary>
    public string? Asking;

    /// <summary>Index of <see cref="Asking"/> among the download's files.</summary>
    public int AskingIndex;

    /// <summary>Files the save dialog wrote, once it is done with all of them.</summary>
    public int Saved;
}

public sealed partial class WFMappingClientSystem
{
    [Dependency] private IFileDialogManager _dialogs = default!;
    [Dependency] private IResourceManager _res = default!;

    private WFMappingDownloadAssembler? _assembler;

    /// <summary>A transfer this client gave up on; its chunks still in flight are dropped quietly.</summary>
    private int? _droppedTransfer;
    private readonly List<ResPath> _written = new();

    /// <summary>Files of a Save to download, kept until the last one arrives and the save dialogs open.</summary>
    private readonly List<(string Name, byte[] Data)> _held = new();

    /// <summary>Whether the last request was a ship download, for the progress of the download it starts.</summary>
    private bool _requestedShip;

    /// <summary>Whether the last request was an export, whose download is one file: a zip or a grid file.</summary>
    private bool _requestedZip;

    /// <summary>The running or last finished download, or null when there is none to show.</summary>
    public WFMappingDownloadProgress? Download { get; private set; }

    public event Action? DownloadUpdated;

    /// <summary>Whether downloads go through the save dialog instead of the data folder; kept between games.</summary>
    public bool DownloadSaveTo
    {
        get => _cfg.GetCVar(WFMappingModeCVars.DownloadSaveTo);
        set
        {
            if (value == DownloadSaveTo)
                return;

            _cfg.SetCVar(WFMappingModeCVars.DownloadSaveTo, value);
            _cfg.SaveToFile();
        }
    }

    /// <summary>Whether a download is receiving or waiting on its save dialogs.</summary>
    public bool DownloadBusy => Download is { Active: true } or { Asking: not null } || _held.Count > 0;

    private void InitializeDownload()
    {
        SubscribeNetworkEvent<WFMappingDownloadChunkEvent>(OnChunk);
        SubscribeNetworkEvent<WFMappingDownloadCancelledEvent>(OnDownloadCancelled);
        InitializeExport();
    }

    /// <summary>Asks for every level of a library version; null is the newest.</summary>
    public void RequestDownload(string mapName, string? version)
    {
        if (RefuseWhileSaving())
            return;

        _requestedShip = false;
        _requestedZip = false;
        RaiseNetworkEvent(new WFMappingDownloadRequest(mapName, version));
    }

    /// <summary>Asks for a library version's one grid and its generated vessel prototype; null is the newest.</summary>
    public void RequestShipDownload(string mapName, string? version)
    {
        if (RefuseWhileSaving())
            return;

        _requestedShip = true;
        _requestedZip = false;
        RaiseNetworkEvent(new WFMappingDownloadRequest(mapName, version, ship: true));
    }

    /// <summary>A new download would replace files still waiting on the save dialog; say so instead.</summary>
    private bool RefuseWhileSaving()
    {
        if (_held.Count == 0 && Download is not { Asking: not null })
            return false;

        ReportLocal("wf-mapping-mode-download-busy", false);
        return true;
    }

    public void CancelDownload()
    {
        RaiseNetworkEvent(new WFMappingDownloadCancelRequest());
    }

    /// <summary>The folder the last download went into; null for one that went through the save dialog.</summary>
    public ResPath? DownloadFolder => Download is { Active: false, SaveTo: false } done
        ? WFMappingLibraryNames.DownloadRoot / done.MapName / done.Version
        : null;

    /// <summary>Opens the last download's folder in the OS file browser.</summary>
    public void OpenDownloadFolder()
    {
        if (DownloadFolder is not { } folder)
            return;

        try
        {
            _res.UserData.OpenOsWindow(folder);
        }
        catch (Exception e)
        {
            Log.Error($"Opening the mapping download folder {folder} failed: {e}");
        }
    }

    private void OnChunk(WFMappingDownloadChunkEvent ev)
    {
        if (ev.TransferId == _droppedTransfer)
            return;

        if (!WFMappingLibraryNames.IsValidMapName(ev.MapName) ||
            !WFMappingLibraryNames.TryParseVersion(ev.Version, out _) ||
            ev.FileIndex < 0 || ev.FileIndex >= ev.FileCount)
        {
            Fail(ev.TransferId, "wf-mapping-mode-download-bad-data");
            return;
        }

        if (_assembler == null || _assembler.TransferId != ev.TransferId)
        {
            if (ev.Chunk != 0 ||
                !WFMappingDownloadAssembler.TryCreate(ev.TransferId, ev.MapName, ev.FileName, ev.ChunkCount,
                    ev.TotalBytes, out var assembler))
            {
                Fail(ev.TransferId, "wf-mapping-mode-download-bad-data");
                return;
            }

            if (ev.FileIndex == 0 || Download is not { Active: true } running ||
                running.MapName != ev.MapName || running.Version != ev.Version)
            {
                _written.Clear();
                _held.Clear();
                Download = new WFMappingDownloadProgress
                {
                    MapName = ev.MapName,
                    Version = ev.Version,
                    Active = true,
                    SaveTo = DownloadSaveTo,
                    Ship = _requestedShip,
                    Zip = _requestedZip || WFMappingLibraryNames.IsZipFile(ev.FileName),
                };
            }

            _assembler = assembler;
        }

        if (_assembler.Add(ev.Chunk, ev.ChunkCount, ev.Data) != WFMappingChunkResult.Accepted)
        {
            Fail(ev.TransferId, "wf-mapping-mode-download-bad-data");
            return;
        }

        var progress = Download!;
        progress.FileIndex = ev.FileIndex;
        progress.FileCount = ev.FileCount;
        progress.Chunks = _assembler.Received;
        progress.ChunkCount = ev.ChunkCount;

        var finished = false;
        if (_assembler.IsComplete)
        {
            if (!_assembler.TryComplete(out var data))
            {
                Fail(ev.TransferId, "wf-mapping-mode-download-bad-data");
                return;
            }

            var fileName = _assembler.FileName;
            if (progress.SaveTo)
            {
                _held.Add((fileName, data));
            }
            else if (TryWrite(WFMappingLibraryNames.DownloadPath(ev.MapName, ev.Version, fileName), data) is { } error)
            {
                Fail(ev.TransferId, error, ("file", fileName));
                return;
            }

            _assembler = null;
            finished = ev.FileIndex == ev.FileCount - 1;
            if (finished)
                progress.Active = false;
        }

        RaiseNetworkEvent(new WFMappingDownloadAckEvent(ev.TransferId, ev.Chunk));
        if (finished)
        {
            // A later download that fails mustn't delete this one's files.
            _written.Clear();
            if (progress.SaveTo)
                SaveHeld(progress);
        }

        DownloadUpdated?.Invoke();
    }

    /// <summary>
    /// Writes a file into the data folder and reads it back; null when every byte is there, else the error's
    /// string key.
    /// </summary>
    private string? TryWrite(ResPath path, byte[] data)
    {
        try
        {
            _res.UserData.CreateDir(path.Directory);
            using (var stream = _res.UserData.OpenWrite(path))
            {
                stream.Write(data, 0, data.Length);
            }

            _written.Add(path);

            long read;
            using (var stream = _res.UserData.OpenRead(path))
            {
                read = CountBytes(stream);
            }

            if (read == data.Length)
                return null;

            Log.Warning($"The mapping download {path} holds {read} bytes after writing {data.Length}");
            return "wf-mapping-mode-download-verify-failed";
        }
        catch (Exception e)
        {
            Log.Error($"Writing the mapping download {path} failed: {e}");
            return "wf-mapping-mode-download-write-failed";
        }
    }

    /// <summary>Bytes from a stream's position to its end, read rather than taken from <c>Length</c>.</summary>
    private static long CountBytes(Stream stream)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
        }

        return total;
    }

    /// <summary>
    /// Offers each held file through the save dialog in turn and writes it to the stream the dialog returns. The
    /// dialog takes no file name, so the download row names the file it is asking for; an export is one zip, so one
    /// dialog.
    /// </summary>
    private async void SaveHeld(WFMappingDownloadProgress progress)
    {
        var files = _held.ToList();
        var saved = 0;
        string? failed = null;
        try
        {
            var yml = new FileDialogFilters(new FileDialogFilters.Group("yml"));
            var zip = new FileDialogFilters(new FileDialogFilters.Group("zip"));
            for (var i = 0; i < files.Count; i++)
            {
                var (name, data) = files[i];
                progress.Asking = name;
                progress.AskingIndex = i;
                DownloadUpdated?.Invoke();

                var chosen = await _dialogs.SaveFile(WFMappingLibraryNames.IsZipFile(name) ? zip : yml, truncate: true);
                if (chosen is not { } target)
                    continue;

                await using (target.fileStream)
                {
                    target.fileStream.Write(data, 0, data.Length);
                    target.fileStream.Flush();
                    if (target.fileStream.CanSeek && target.fileStream.Length != data.Length)
                    {
                        Log.Warning($"The mapping download {name} holds {target.fileStream.Length} bytes after writing {data.Length}");
                        failed = name;
                        break;
                    }
                }

                saved++;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Saving the mapping download through the save dialog failed: {e}");
            failed ??= progress.Asking ?? string.Empty;
        }
        finally
        {
            _held.Clear();
            progress.Asking = null;
            progress.Saved = saved;
        }

        if (failed != null)
            ReportLocal("wf-mapping-mode-download-save-failed", false, ("file", failed));
        else if (saved == files.Count)
            ReportLocal("wf-mapping-mode-download-saved", true, ("count", saved));
        else
            ReportLocal("wf-mapping-mode-download-save-skipped", false, ("saved", saved), ("count", files.Count));

        DownloadUpdated?.Invoke();
    }

    /// <summary>Stops a download the client can't take, tells the player and drops what it wrote.</summary>
    private void Fail(int transferId, string reason, params (string, object)[] args)
    {
        _droppedTransfer = transferId;
        var wasActive = _assembler != null || Download is { Active: true };
        _assembler = null;
        DropWritten();
        _held.Clear();
        Download = null;

        if (wasActive)
            CancelDownload();

        Report(new WFMappingResult(reason, Loc.GetString(reason, args), false));
        DownloadUpdated?.Invoke();
    }

    private void OnDownloadCancelled(WFMappingDownloadCancelledEvent ev)
    {
        if (Download is not { Active: true } && _assembler == null)
            return;

        _assembler = null;
        DropWritten();
        _held.Clear();
        Download = null;
        DownloadUpdated?.Invoke();
    }

    /// <summary>Deletes the files an unfinished download already wrote.</summary>
    private void DropWritten()
    {
        foreach (var path in _written)
        {
            try
            {
                _res.UserData.Delete(path);
            }
            catch (Exception e)
            {
                Log.Warning($"Removing the partial mapping download {path} failed: {e}");
            }
        }

        _written.Clear();
    }
}
