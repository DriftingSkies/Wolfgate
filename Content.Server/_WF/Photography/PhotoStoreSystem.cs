using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WF.Photography;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.Server._WF.Photography;

/// <summary>Keeps photo images by hash, in memory and in the database, and sends them to clients.</summary>
public sealed partial class PhotoStoreSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private PhotoAlbumSystem _albums = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>A stored image and its thumbnail.</summary>
    public sealed record StoredPhoto(byte[] Png, int Width, int Height, string Thumbnail);

    private readonly Dictionary<string, StoredPhoto> _images = new();

    /// <summary>Database reads in flight, so many clients asking for one photo read it once.</summary>
    private readonly Dictionary<string, Task<StoredPhoto?>> _loads = new();

    /// <summary>When each session's download window started and how many images it has had in it.</summary>
    private readonly Dictionary<ICommonSession, (TimeSpan Start, int Count)> _downloads = new();

    /// <summary>Images each session has been sent this round. Clients keep them, so they aren't sent twice.</summary>
    private readonly Dictionary<ICommonSession, HashSet<string>> _sent = new();

    /// <summary>Hashes the database has no image for, deleted or never saved.</summary>
    private readonly HashSet<string> _absent = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PhotoImageRequestEvent>(OnImageRequest);
        SubscribeLocalEvent<PhotoComponent, ComponentStartup>(OnPhotoStartup);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _player.PlayerStatusChanged += OnPlayerStatusChanged;

        PurgeUnused();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _images.Clear();
        _downloads.Clear();
        _sent.Clear();
        _absent.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _downloads.Remove(args.Session);
        _sent.Remove(args.Session);
    }

    /// <summary>Stores a new image under its hash, writing it to the database in the background.</summary>
    public void Store(string hash, byte[] png, int width, int height, Guid? author)
    {
        _images[hash] = new StoredPhoto(png, width, height, MakeThumbnail(png));
        _absent.Remove(hash);

        var row = new WolfgatePhoto
        {
            Hash = hash,
            Png = png,
            Width = width,
            Height = height,
            AuthorUserId = author,
            RoundId = _ticker.RoundId,
            CreatedAt = DateTime.UtcNow,
        };
        Save(row);
    }

    /// <summary>The image under a hash, reading it from the database when it isn't in memory.</summary>
    public Task<StoredPhoto?> GetAsync(string hash)
    {
        if (_images.TryGetValue(hash, out var stored))
            return Task.FromResult<StoredPhoto?>(stored);

        if (_loads.TryGetValue(hash, out var load))
            return load;

        load = Load(hash);
        _loads[hash] = load;
        return load;
    }

    private async Task<StoredPhoto?> Load(string hash)
    {
        try
        {
            var row = await _db.GetPhotoAsync(hash);
            if (row == null)
            {
                _absent.Add(hash);
                return null;
            }

            var stored = new StoredPhoto(row.Png, row.Width, row.Height, MakeThumbnail(row.Png));
            _images[hash] = stored;
            return stored;
        }
        catch (Exception e)
        {
            Log.Error($"Reading photo {hash} threw: {e}");
            return null;
        }
        finally
        {
            _loads.Remove(hash);
        }
    }

    private async void Save(WolfgatePhoto row)
    {
        try
        {
            await _db.AddPhotoAsync(row);
        }
        catch (Exception e)
        {
            Log.Error($"Saving photo {row.Hash} threw: {e}");
        }
    }

    /// <summary>
    /// Marks a photo that loads with an image as aged, and checks its image is still stored, blanking it if not.
    /// </summary>
    private void OnPhotoStartup(Entity<PhotoComponent> ent, ref ComponentStartup args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Hash))
            return;

        ent.Comp.Old = true;
        CheckStored(ent, ent.Comp.Hash);
    }

    private async void CheckStored(Entity<PhotoComponent> ent, string hash)
    {
        var stored = await GetAsync(hash);
        if (TerminatingOrDeleted(ent) || ent.Comp.Hash != hash)
            return;

        if (stored == null)
        {
            if (_absent.Contains(hash))
                BlankPhoto(ent);
            return;
        }

        if (string.IsNullOrEmpty(ent.Comp.Thumbnail))
        {
            ent.Comp.Thumbnail = stored.Thumbnail;
            Dirty(ent);
        }
    }

    /// <summary>
    /// Deletes an image for good and blanks every photo and gallery entry in the round that shows it.
    /// Saved copies blank themselves when they next load. Returns how many photos and entries were blanked.
    /// </summary>
    public int Blank(string hash)
    {
        _images.Remove(hash);
        _absent.Add(hash);
        DeleteStored(hash);

        var blanked = 0;
        var photos = EntityQueryEnumerator<PhotoComponent>();
        while (photos.MoveNext(out var uid, out var photo))
        {
            if (photo.Hash != hash)
                continue;

            BlankPhoto((uid, photo));
            blanked++;
        }

        var galleries = EntityQueryEnumerator<PhotoGalleryComponent>();
        while (galleries.MoveNext(out var uid, out var gallery))
        {
            var removed = gallery.Photos.RemoveAll(p => p.Hash == hash);
            if (removed == 0)
                continue;

            blanked += removed;
            Dirty(uid, gallery);
        }

        return blanked;
    }

    private void BlankPhoto(Entity<PhotoComponent> ent)
    {
        ent.Comp.Hash = string.Empty;
        ent.Comp.Thumbnail = string.Empty;
        ent.Comp.Width = 0;
        ent.Comp.Height = 0;
        Dirty(ent);
        _albums.PhotoChanged(ent);
    }

    private async void DeleteStored(string hash)
    {
        try
        {
            await _db.DeletePhotoAsync(hash);
        }
        catch (Exception e)
        {
            Log.Error($"Deleting photo {hash} threw: {e}");
        }
    }

    /// <summary>Sets a photo's thumbnail from its image, reading the image from the database if needed.</summary>
    public async void FillThumbnail(Entity<PhotoComponent> ent)
    {
        var hash = ent.Comp.Hash;
        if (string.IsNullOrEmpty(hash) || !string.IsNullOrEmpty(ent.Comp.Thumbnail))
            return;

        if (_images.TryGetValue(hash, out var cached))
        {
            ent.Comp.Thumbnail = cached.Thumbnail;
            return;
        }

        var stored = await GetAsync(hash);
        if (stored == null || TerminatingOrDeleted(ent) || ent.Comp.Hash != hash)
            return;

        ent.Comp.Thumbnail = stored.Thumbnail;
        Dirty(ent);
    }

    /// <summary>Shrinks an image to the thumbnail size and packs its pixels as base64 RGB.</summary>
    private string MakeThumbnail(byte[] png)
    {
        try
        {
            var size = PhotoRules.ThumbnailPixels;
            using var image = Image.Load<Rgba32>(png);
            image.Mutate(context => context.Resize(size, size));

            var bytes = new byte[PhotoRules.ThumbnailBytes];
            var i = 0;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var pixel = image[x, y];
                    bytes[i++] = pixel.R;
                    bytes[i++] = pixel.G;
                    bytes[i++] = pixel.B;
                }
            }

            return Convert.ToBase64String(bytes);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not make a photo thumbnail: {e.Message}");
            return string.Empty;
        }
    }

    private async void OnImageRequest(PhotoImageRequestEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (msg.Hash is not { Length: 64 } hash
            || _sent.TryGetValue(session, out var sent) && sent.Contains(hash))
            return;

        // Over the limit the request goes unanswered, and the client asks again later.
        if (!TakeDownload(session))
            return;

        if (!IsShown(hash))
        {
            RaiseNetworkEvent(new PhotoImageEvent(hash, null), session);
            return;
        }

        var stored = await GetAsync(hash);
        if (session.Status == SessionStatus.Disconnected)
            return;

        if (stored != null)
            _sent.GetOrNew(session).Add(hash);

        RaiseNetworkEvent(new PhotoImageEvent(hash, stored?.Png), session);
    }

    /// <summary>Whether a photo or gallery in the round shows this hash.</summary>
    private bool IsShown(string hash)
    {
        var photos = EntityQueryEnumerator<PhotoComponent>();
        while (photos.MoveNext(out _, out var photo))
        {
            if (photo.Hash == hash)
                return true;
        }

        var galleries = EntityQueryEnumerator<PhotoGalleryComponent>();
        while (galleries.MoveNext(out _, out var gallery))
        {
            if (gallery.Photos.Any(p => p.Hash == hash))
                return true;
        }

        return false;
    }

    private bool TakeDownload(ICommonSession session)
    {
        var now = _timing.RealTime;
        var limit = _cfg.GetCVar(PhotoCVars.DownloadsPerMinute);
        if (!_downloads.TryGetValue(session, out var window) || now - window.Start >= TimeSpan.FromMinutes(1))
            window = (now, 0);

        if (window.Count >= limit)
            return false;

        _downloads[session] = (window.Start, window.Count + 1);
        return true;
    }

    /// <summary>Deletes stored photos nothing has held for the kept number of days, once at startup.</summary>
    private async void PurgeUnused()
    {
        var days = _cfg.GetCVar(PhotoCVars.KeepUnusedDays);
        if (days <= 0)
            return;

        try
        {
            var (found, deleted) = await _db.PurgeUnusedPhotosAsync(TimeSpan.FromDays(days));
            if (found > 0 || deleted > 0)
                Log.Info($"Photos: deleted {deleted} unheld for {days} days, found {found} more that nothing holds.");
        }
        catch (Exception e)
        {
            Log.Error($"Cleaning up unused photos threw: {e}");
        }
    }
}
