using System.Linq;
using System.Threading.Tasks;
using Content.Server._Mono.Persistence;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Photography;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Photography;

/// <summary>
/// Saves personal photo albums through the round, wherever they are, and gives them back when their character spawns.
/// </summary>
public sealed partial class PhotoAlbumSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private PersistentProfileSystem _persistence = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly EntProtoId Album = "WFPhotoAlbumPersonal";

    private TimeSpan _lastSave;

    /// <summary>Spawned characters to give albums to next tick, once traits are in place.</summary>
    private readonly List<PlayerSpawnCompleteEvent> _queued = new();

    /// <summary>Changed albums waiting to be saved, one per tick.</summary>
    private readonly Queue<EntityUid> _saveQueue = new();
    private readonly HashSet<EntityUid> _queuedSaves = new();

    /// <summary>The last database write of each album, so its writes run one at a time and in order.</summary>
    private readonly Dictionary<Guid, Task> _writes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(_queued.Add);
        SubscribeLocalEvent<RoundEndedEvent>(_ => QueueChanged());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => SaveRemaining());
        SubscribeLocalEvent<PhotoAlbumOwnerComponent, EntInsertedIntoContainerMessage>((_, comp, _) =>
            comp.Changed = true);
        SubscribeLocalEvent<PhotoAlbumOwnerComponent, EntRemovedFromContainerMessage>((_, comp, _) =>
            comp.Changed = true);
        SubscribeLocalEvent<PhotoAlbumOwnerComponent, ComponentStartup>(OnOwnerStartup);
    }

    /// <summary>
    /// Deletes a loaded album whose saved copy is already in the world, such as one taken out of a deposit box after
    /// its character spawned with it.
    /// </summary>
    private void OnOwnerStartup(Entity<PhotoAlbumOwnerComponent> ent, ref ComponentStartup args)
    {
        var id = ent.Comp.AlbumId;
        if (id.Length == 0)
            return;

        var query = EntityQueryEnumerator<PhotoAlbumOwnerComponent>();
        while (query.MoveNext(out var uid, out var owner))
        {
            if (uid == ent.Owner || owner.AlbumId != id || TerminatingOrDeleted(uid))
                continue;

            Log.Warning($"Deleting {ToPrettyString(ent)}, a second copy of photo album {id}.");
            QueueDel(ent);
            return;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var spawn in _queued)
        {
            GiveAlbums(spawn);
        }

        _queued.Clear();

        SaveNext();

        if (_timing.CurTime < _lastSave + TimeSpan.FromMinutes(_cfg.GetCVar(PhotoCVars.AlbumSaveMinutes)))
            return;

        _lastSave = _timing.CurTime;
        QueueChanged();
    }

    /// <summary>Marks the album holding a photo as changed, after the photo was renamed or written on.</summary>
    public void PhotoChanged(EntityUid photo)
    {
        var parent = Transform(photo).ParentUid;
        if (TryComp<PhotoAlbumOwnerComponent>(parent, out var owner))
            owner.Changed = true;
    }

    /// <summary>Queues every changed album that isn't queued yet.</summary>
    private void QueueChanged()
    {
        var query = EntityQueryEnumerator<PhotoAlbumOwnerComponent>();
        while (query.MoveNext(out var uid, out var owner))
        {
            if (owner.Changed && _queuedSaves.Add(uid))
                _saveQueue.Enqueue(uid);
        }
    }

    /// <summary>Saves the next queued album that still exists and is still changed.</summary>
    private void SaveNext()
    {
        while (_saveQueue.TryDequeue(out var uid))
        {
            _queuedSaves.Remove(uid);
            if (!TryComp<PhotoAlbumOwnerComponent>(uid, out var owner) || !owner.Changed)
                continue;

            Save((uid, owner));
            return;
        }
    }

    /// <summary>Saves every changed album at once, before the round's entities are deleted.</summary>
    private void SaveRemaining()
    {
        _saveQueue.Clear();
        _queuedSaves.Clear();
        var query = EntityQueryEnumerator<PhotoAlbumOwnerComponent>();
        while (query.MoveNext(out var uid, out var owner))
        {
            if (owner.Changed)
                Save((uid, owner));
        }
    }

    private async void Save(Entity<PhotoAlbumOwnerComponent> album)
    {
        album.Comp.Changed = false;
        if (!Guid.TryParse(album.Comp.AlbumId, out var albumId)
            || !_persistence.TrySerializeEntity(album, out var data))
        {
            album.Comp.Changed = true;
            Log.Error($"Could not serialize photo album {ToPrettyString(album)}.");
            return;
        }

        var row = new WolfgatePhotoAlbum
        {
            AlbumId = albumId,
            ProfileId = album.Comp.ProfileId,
            Data = data,
            RoundId = _ticker.RoundId,
            UpdatedAt = DateTime.UtcNow,
        };

        var write = Write(_writes.GetValueOrDefault(albumId), row);
        _writes[albumId] = write;
        if (!await write && !TerminatingOrDeleted(album))
            album.Comp.Changed = true;

        if (_writes.TryGetValue(albumId, out var last) && last == write)
            _writes.Remove(albumId);
    }

    /// <summary>Writes an album once its earlier write has finished. Returns whether this write succeeded.</summary>
    private async Task<bool> Write(Task? previous, WolfgatePhotoAlbum row)
    {
        if (previous != null)
            await previous;

        try
        {
            await _db.SavePhotoAlbumAsync(row);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Saving photo album {row.AlbumId} threw: {e}");
            return false;
        }
    }

    /// <summary>Gives a spawned character its saved albums, and a Photographer a personal album.</summary>
    private async void GiveAlbums(PlayerSpawnCompleteEvent args)
    {
        var mob = args.Mob;
        var user = args.Player.UserId;
        if (!_preferences.TryGetCachedPreferences(user, out var preferences)
            || !preferences.TryIndexOfCharacter(args.Profile, out var slot))
            return;

        Dictionary<int, int> profileIds;
        List<WolfgatePhotoAlbum> saved;
        try
        {
            profileIds = await _db.GetProfileIdsAsync(user.UserId);
            saved = await _db.GetPhotoAlbumsAsync(user.UserId);
        }
        catch (Exception e)
        {
            Log.Error($"Reading photo albums for {user} threw: {e}");
            return;
        }

        if (TerminatingOrDeleted(mob) || !profileIds.TryGetValue(slot, out var profileId))
            return;

        var albums = saved.Where(a => a.ProfileId == profileId)
            .Take(_cfg.GetCVar(PhotoCVars.AlbumsPerCharacter))
            .ToList();
        foreach (var album in albums)
        {
            Restore(mob, album);
        }

        if (HasComp<PhotographerComponent>(mob))
            GivePersonalAlbum(mob, profileId, albums.Count);
    }

    /// <summary>Gives a saved album back, unless a copy of it is in the world.</summary>
    private void Restore(EntityUid mob, WolfgatePhotoAlbum album)
    {
        var id = album.AlbumId.ToString();
        if (InWorld(owner => owner.AlbumId == id))
            return;

        if (!_persistence.TryDeserializeEntity(album.Data, out var uid))
        {
            Log.Error($"Could not load photo album {album.AlbumId} for {ToPrettyString(mob)}.");
            return;
        }

        Give(mob, uid);
    }

    /// <summary>
    /// Makes a carried personal album without an owner this character's, or gives a new one when the character has
    /// no saved album.
    /// </summary>
    private void GivePersonalAlbum(EntityUid mob, int profileId, int savedCount)
    {
        if (savedCount >= _cfg.GetCVar(PhotoCVars.AlbumsPerCharacter)
            || InWorld(owner => owner.ProfileId == profileId))
            return;

        EntityUid? carried = null;
        foreach (var uid in _inventory.GetHandOrInventoryEntities(mob))
        {
            if (MetaData(uid).EntityPrototype?.ID == Album.Id && !HasComp<PhotoAlbumOwnerComponent>(uid))
            {
                carried = uid;
                break;
            }
        }

        if (savedCount > 0 && carried == null)
            return;

        var album = carried ?? Spawn(Album, Transform(mob).Coordinates);
        var owner = AddComp<PhotoAlbumOwnerComponent>(album);
        owner.AlbumId = Guid.NewGuid().ToString();
        owner.ProfileId = profileId;
        Save((album, owner));

        if (carried == null)
            Give(mob, album);
    }

    /// <summary>Whether any owned album in the world matches.</summary>
    private bool InWorld(Predicate<PhotoAlbumOwnerComponent> match)
    {
        var query = EntityQueryEnumerator<PhotoAlbumOwnerComponent>();
        while (query.MoveNext(out _, out var owner))
        {
            if (match(owner))
                return true;
        }

        return false;
    }

    /// <summary>Puts an album in the mob's backpack, or its hands, or at its feet.</summary>
    private void Give(EntityUid mob, EntityUid album)
    {
        if (_inventory.TryGetSlotEntity(mob, "back", out var back)
            && _storage.Insert(back.Value, album, out _, playSound: false))
            return;

        _hands.PickupOrDrop(mob, album, checkActionBlocker: false);
    }
}
