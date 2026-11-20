using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared._WG.Paintings;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Paintings;

/// <summary>A catalogue painting without its pixels.</summary>
public sealed record WGPaintingEntry(
    string Hash,
    int Width,
    int Height,
    string Title,
    string Artist,
    Guid? Author,
    Guid ScannedBy,
    DateTime CreatedAt,
    bool Blocked);

/// <summary>Outcome of a scan, as a Fluent id for the scanner's popup.</summary>
public enum WGPaintingScanResult : byte
{
    Stored,
    Unfinished,
    Blank,
    Known,
    Blocked,
    DailyLimit,
    Unavailable,
}

/// <summary>
/// The art catalogue: paintings scanned from finished canvases, kept in the database by the hash of their pixels.
/// The index of every painting stays in memory; pixels load on demand.
/// </summary>
public sealed partial class WGPaintingStoreSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Every painting, blocked ones included, by hash.</summary>
    private readonly Dictionary<string, WGPaintingEntry> _index = new();

    /// <summary>The unblocked paintings, newest first.</summary>
    private readonly List<WGPaintingEntry> _catalogue = new();

    private readonly Dictionary<string, byte[]> _pixels = new();
    private readonly Dictionary<string, Task<byte[]?>> _loads = new();
    private readonly Dictionary<ICommonSession, (TimeSpan Start, int Count)> _downloads = new();

    /// <summary>Last seen usernames of authors and scanners, empty for players the database doesn't know.</summary>
    private readonly Dictionary<Guid, string> _names = new();

    /// <summary>The index has loaded from the database.</summary>
    public bool Loaded { get; private set; }

    /// <summary>Raised once the index has loaded.</summary>
    public event Action? IndexLoaded;

    public IReadOnlyList<WGPaintingEntry> Catalogue => _catalogue;

    /// <summary>Every painting, blocked ones included.</summary>
    public IReadOnlyCollection<WGPaintingEntry> All => _index.Values;

    /// <summary>Raised when a painting is added, removed, blocked or unblocked.</summary>
    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGPaintingArtRequestEvent>(OnArtRequest);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        LoadIndex();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _downloads.Remove(args.Session);
    }

    private async void LoadIndex()
    {
        try
        {
            foreach (var row in await _db.GetPaintingIndexAsync())
            {
                _index[row.Hash] = new WGPaintingEntry(row.Hash, row.Width, row.Height, row.Title, row.Artist,
                    row.AuthorUserId, row.ScannedByUserId, row.CreatedAt, row.Blocked);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Loading the painting catalogue threw: {e}");
            return;
        }

        RebuildCatalogue();
        Loaded = true;
        IndexLoaded?.Invoke();
    }

    private void RebuildCatalogue()
    {
        _catalogue.Clear();
        _catalogue.AddRange(_index.Values.Where(e => !e.Blocked).OrderByDescending(e => e.CreatedAt));
    }

    /// <summary>The hash naming a painting: SHA-256 of its size and RGBA pixels, lowercase hex.</summary>
    public static string Hash(int width, int height, byte[] pixels)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(BitConverter.GetBytes(width));
        sha.AppendData(BitConverter.GetBytes(height));
        sha.AppendData(pixels);
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    public bool TryGetEntry(string hash, out WGPaintingEntry entry)
    {
        return _index.TryGetValue(hash, out entry!) && !entry.Blocked;
    }

    public bool IsBlocked(string hash)
    {
        return _index.TryGetValue(hash, out var entry) && entry.Blocked;
    }

    /// <summary>A random catalogue painting of the small or the large sizes, or null when there is none.</summary>
    public WGPaintingEntry? PickRandom(bool large)
    {
        var fitting = _catalogue.Where(e => WGPaintingRules.GetSize(e.Width, e.Height)?.Large == large).ToList();
        return fitting.Count == 0 ? null : _random.Pick(fitting);
    }

    /// <summary>A catalogue painting's pixels, or null when there is none or it is blocked.</summary>
    public Task<byte[]?> GetPixelsAsync(string hash)
    {
        if (!TryGetEntry(hash, out _))
            return Task.FromResult<byte[]?>(null);

        if (_pixels.TryGetValue(hash, out var cached))
            return Task.FromResult<byte[]?>(cached);

        if (_loads.TryGetValue(hash, out var running))
            return running;

        var load = LoadPixels(hash);
        _loads[hash] = load;
        return load;
    }

    private async Task<byte[]?> LoadPixels(string hash)
    {
        try
        {
            var pixels = await _db.GetPaintingPixelsAsync(hash);
            // Checked again, as the painting may have been blocked or deleted while it loaded.
            if (pixels != null && _index.TryGetValue(hash, out var entry) && !entry.Blocked
                && pixels.Length == entry.Width * entry.Height * 4)
            {
                _pixels[hash] = pixels;
                return pixels;
            }

            return null;
        }
        catch (Exception e)
        {
            Log.Error($"Loading painting {hash} threw: {e}");
            return null;
        }
        finally
        {
            _loads.Remove(hash);
        }
    }

    /// <summary>Paintings a player scanned in the last day.</summary>
    private int ScansToday(Guid user)
    {
        var since = DateTime.UtcNow - TimeSpan.FromDays(1);
        return _index.Values.Count(e => e.ScannedBy == user && e.CreatedAt > since);
    }

    /// <summary>Adds a finished canvas to the catalogue, credited to whoever painted it.</summary>
    public async Task<WGPaintingScanResult> Scan(Entity<WGCanvasComponent> canvas, EntityUid scanner, Guid scannerUser)
    {
        var comp = canvas.Comp;
        if (!comp.Finished || string.IsNullOrEmpty(comp.Hash))
            return WGPaintingScanResult.Unfinished;
        if (!comp.Used)
            return WGPaintingScanResult.Blank;
        if (!Loaded)
            return WGPaintingScanResult.Unavailable;
        if (_index.TryGetValue(comp.Hash, out var known))
            return known.Blocked ? WGPaintingScanResult.Blocked : WGPaintingScanResult.Known;

        var limit = _cfg.GetCVar(WGPaintingCVars.DailyScans);
        if (limit > 0 && ScansToday(scannerUser) >= limit)
            return WGPaintingScanResult.DailyLimit;

        var row = new WolfgatePainting
        {
            Hash = comp.Hash,
            Width = comp.Width,
            Height = comp.Height,
            Pixels = (byte[]) comp.Pixels.Clone(),
            Title = comp.Title,
            Artist = comp.Artist,
            AuthorUserId = comp.AuthorUserId,
            ScannedByUserId = scannerUser,
            CreatedAt = DateTime.UtcNow,
        };

        // Held as known while the write runs, so a second scan of the same painting is not written twice.
        var entry = new WGPaintingEntry(row.Hash, row.Width, row.Height, row.Title, row.Artist, row.AuthorUserId,
            row.ScannedByUserId, row.CreatedAt, false);
        _index[row.Hash] = entry;

        bool added;
        try
        {
            added = await _db.AddPaintingAsync(row);
        }
        catch (Exception e)
        {
            Log.Error($"Saving painting {row.Hash} threw: {e}");
            _index.Remove(row.Hash);
            return WGPaintingScanResult.Unavailable;
        }

        if (!added)
            return WGPaintingScanResult.Known;

        _pixels[row.Hash] = row.Pixels;
        RebuildCatalogue();
        Changed?.Invoke();
        _adminLog.Add(LogType.WGPainting, LogImpact.Low,
            $"{ToPrettyString(scanner):player} scanned painting \"{row.Title}\" by {row.Artist} ({row.Hash}) into the catalogue");
        return WGPaintingScanResult.Stored;
    }

    /// <summary>Takes a painting out of the catalogue. Only its painter may.</summary>
    public async Task<bool> Remove(string hash, EntityUid user, Guid userId)
    {
        if (!_index.TryGetValue(hash, out var entry) || entry.Blocked || entry.Author != userId)
            return false;

        if (!await Forget(hash))
            return false;

        _adminLog.Add(LogType.WGPainting, LogImpact.Low,
            $"{ToPrettyString(user):player} removed their painting \"{entry.Title}\" ({hash}) from the catalogue");
        return true;
    }

    /// <summary>Deletes a painting from the database and every copy of it in the round, as an admin.</summary>
    public async Task<bool> Delete(string hash, string actor)
    {
        if (!_index.TryGetValue(hash, out var entry))
            return false;

        if (!await Forget(hash))
            return false;

        DeleteCopies(hash);
        _adminLog.Add(LogType.WGPainting, LogImpact.High,
            $"{actor} deleted painting \"{entry.Title}\" by {entry.Artist} ({hash}) from the database");
        return true;
    }

    /// <summary>Drops a painting from the database, then the index. False when the database delete failed.</summary>
    private async Task<bool> Forget(string hash)
    {
        try
        {
            await _db.DeletePaintingAsync(hash);
        }
        catch (Exception e)
        {
            Log.Error($"Deleting painting {hash} threw: {e}");
            return false;
        }

        _index.Remove(hash);
        _pixels.Remove(hash);
        RebuildCatalogue();
        Changed?.Invoke();
        return true;
    }

    private void DeleteCopies(string hash)
    {
        var query = EntityQueryEnumerator<WGCanvasComponent>();
        while (query.MoveNext(out var uid, out var canvas))
        {
            if (canvas.Hash == hash)
                QueueDel(uid);
        }
    }

    /// <summary>A painting's pixels whether or not it is blocked, for admins to look at.</summary>
    public async Task<byte[]?> GetAnyPixelsAsync(string hash)
    {
        if (_pixels.TryGetValue(hash, out var cached))
            return cached;

        try
        {
            return await _db.GetPaintingPixelsAsync(hash, includeBlocked: true);
        }
        catch (Exception e)
        {
            Log.Error($"Loading painting {hash} threw: {e}");
            return null;
        }
    }

    /// <summary>A player's last seen username, or empty when it isn't loaded or known.</summary>
    public string PlayerName(Guid? user)
    {
        return user is { } id && _names.TryGetValue(id, out var name) ? name : string.Empty;
    }

    /// <summary>Loads the usernames of authors and scanners not yet looked up. True when any were.</summary>
    public async Task<bool> LoadPlayerNames()
    {
        var missing = _index.Values
            .SelectMany(e => e.Author is { } author ? new[] { author, e.ScannedBy } : new[] { e.ScannedBy })
            .Where(id => !_names.ContainsKey(id))
            .Distinct()
            .ToList();
        if (missing.Count == 0)
            return false;

        Dictionary<Guid, string> found;
        try
        {
            found = await _db.GetPlayerNamesAsync(missing);
        }
        catch (Exception e)
        {
            Log.Error($"Loading painters' names threw: {e}");
            return false;
        }

        foreach (var id in missing)
            _names[id] = found.GetValueOrDefault(id, string.Empty);
        return true;
    }

    /// <summary>Blocks or unblocks a painting. A blocked painting's canvases in the round are deleted.</summary>
    public async Task<bool> SetBlocked(string hash, bool blocked, string actor)
    {
        if (!await _db.SetPaintingBlockedAsync(hash, blocked))
            return false;

        if (_index.TryGetValue(hash, out var entry))
            _index[hash] = entry with { Blocked = blocked };
        _pixels.Remove(hash);
        RebuildCatalogue();
        Changed?.Invoke();

        if (blocked)
            DeleteCopies(hash);

        _adminLog.Add(LogType.WGPainting, LogImpact.Medium,
            $"{actor} {(blocked ? "blocked" : "unblocked")} painting {hash}");
        return true;
    }

    private async void OnArtRequest(WGPaintingArtRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        foreach (var hash in ev.Hashes.Take(WGPaintingRules.MaxRequestedArt).Distinct())
        {
            if (!WGPaintingRules.IsValidHash(hash) || !TakeDownload(session))
                continue;

            var pixels = await GetPixelsAsync(hash);
            if (session.Status != SessionStatus.InGame && session.Status != SessionStatus.Connected)
                return;

            if (!TryGetEntry(hash, out _))
                pixels = null;
            _index.TryGetValue(hash, out var entry);
            RaiseNetworkEvent(new WGPaintingArtEvent(hash, entry?.Width ?? 0, entry?.Height ?? 0, pixels), session);
        }
    }

    private bool TakeDownload(ICommonSession session)
    {
        var now = _timing.RealTime;
        var limit = _cfg.GetCVar(WGPaintingCVars.DownloadsPerMinute);
        if (!_downloads.TryGetValue(session, out var window) || now - window.Start > TimeSpan.FromMinutes(1))
            window = (now, 0);
        if (window.Count >= limit)
            return false;

        _downloads[session] = (window.Start, window.Count + 1);
        return true;
    }
}
