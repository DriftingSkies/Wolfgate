using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Revenant.EntitySystems;
using Content.Shared._WF.Photography;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Revenant.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StatusEffect;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Stunnable;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Server._WF.Photography;

/// <summary>Takes photos: checks the shot, has the photographer's client render it, then prints or stores it.</summary>
public sealed partial class PhotoCameraSystem : SharedPhotoCameraSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private PhotoCaptionSystem _caption = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private PhotoGallerySystem _gallery = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private RevenantSystem _revenant = default!;
    [Dependency] private StationAiVisionSystem _aiVision = default!;
    [Dependency] private PhotoSystem _photo = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private PhotoStoreSystem _store = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How long a revenant caught by a soul-stealing camera stays solid and stunned.</summary>
    private static readonly TimeSpan SoulStealDuration = TimeSpan.FromSeconds(2);

    /// <summary>A capture sent to a client and not yet answered.</summary>
    private sealed record Pending(
        EntityUid Camera,
        EntityUid User,
        ICommonSession Session,
        MapId Map,
        Vector2 Centre,
        int Tiles,
        List<EntityUid> Seen,
        List<EntityUid> Ghosts,
        TimeSpan Deadline);

    private readonly Dictionary<int, Pending> _pending = new();
    private readonly Dictionary<EntityUid, TimeSpan> _flashes = new();
    private int _nextRequest;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoCameraComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<PhotoCameraComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PhotoCameraComponent, PhotoAiCaptureActionEvent>(OnAiCapture);
        SubscribeNetworkEvent<PhotoCaptureResultEvent>(OnCaptureResult);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = Timing.CurTime;
        foreach (var (camera, until) in _flashes.ToList())
        {
            if (now < until)
                continue;

            _flashes.Remove(camera);
            if (!TerminatingOrDeleted(camera))
                _light.SetEnabled(camera, false);
        }

        foreach (var (id, pending) in _pending.ToList())
        {
            if (now < pending.Deadline)
                continue;

            _pending.Remove(id);
            ReleaseGhosts(pending);
            if (!TerminatingOrDeleted(pending.User))
                Popup.PopupEntity(Loc.GetString("wf-photo-camera-failed"), pending.User, pending.User);
        }
    }

    private void OnAfterInteract(Entity<PhotoCameraComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryTakePhoto(ent, args.User, args.ClickLocation);
    }

    private void OnAiCapture(Entity<PhotoCameraComponent> ent, ref PhotoAiCaptureActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryTakePhoto(ent, args.Performer, args.Target);
    }

    private void OnInteractUsing(Entity<PhotoCameraComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<PhotoFilmComponent>(args.Used) || ent.Comp.StoresPhotos)
            return;

        args.Handled = true;
        if (ent.Comp.FilmLeft > 0)
        {
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-not-empty"), ent, args.User);
            return;
        }

        ent.Comp.FilmLeft = ent.Comp.FilmMax;
        Dirty(ent);
        QueueDel(args.Used);
        PlayCameraSound(ent, ent.Comp.FilmSound, args.User, predicted: false);
        Popup.PopupEntity(Loc.GetString("wf-photo-camera-film-loaded", ("count", ent.Comp.FilmLeft)), ent, args.User);
    }

    /// <summary>Starts a photo of the square around a spot. False when the shot can't be taken.</summary>
    public bool TryTakePhoto(Entity<PhotoCameraComponent> ent, EntityUid user, EntityCoordinates target)
    {
        var camera = ent.Comp;
        if (!TryComp<ActorComponent>(user, out var actor))
            return false;

        if (Timing.CurTime < camera.NextShot)
        {
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-charging"), user, user);
            return false;
        }

        if (_pending.Values.Any(p => p.Camera == ent.Owner))
        {
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-busy"), user, user);
            return false;
        }

        if (!camera.StoresPhotos && camera.FilmLeft <= 0)
        {
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-no-film"), user, user);
            return false;
        }

        var viewer = Viewer(user);
        var viewerCoords = _transform.GetMapCoordinates(viewer);
        var targetCoords = _transform.ToMapCoordinates(target);
        if (viewerCoords.MapId != targetCoords.MapId
            || (viewerCoords.Position - targetCoords.Position).Length() > camera.Range)
        {
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-too-far"), user, user);
            return false;
        }

        var tiles = PhotoRules.TilesForAperture(camera.Aperture);
        var square = GetSquare(targetCoords, tiles);
        var visible = new bool[tiles * tiles];
        for (var i = 0; i < visible.Length; i++)
        {
            visible[i] = camera.CameraNetSight
                ? AiCanSee(square, i)
                : _examine.InRangeUnOccluded(viewerCoords, square.TileCentre(i, targetCoords.MapId),
                    camera.Range + tiles,
                    e => e == user || e == viewer);
        }

        if (!visible[visible.Length / 2])
        {
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-cant-see"), user, user);
            return false;
        }

        var seen = new List<EntityUid>();
        var ghosts = new List<EntityUid>();
        var hidden = new HashSet<EntityUid>();
        foreach (var uid in InSquare(square, visible, targetCoords.MapId))
        {
            if (camera.SeeGhosts && IsSpirit(uid))
            {
                _pvs.AddForceSend(uid, actor.PlayerSession);
                ghosts.Add(uid);
            }
            else if (IsCloaked(uid))
            {
                hidden.Add(uid);
                continue;
            }

            seen.Add(uid);
        }

        camera.NextShot = Timing.CurTime + camera.Cooldown;
        Dirty(ent);

        if (camera.Flash)
        {
            _light.SetEnabled(ent, true);
            _flashes[ent] = Timing.CurTime + camera.FlashDuration;
        }

        PlayCameraSound(ent, camera.ShutterSound, user, predicted: false);

        var id = ++_nextRequest;
        var deadline = Timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(PhotoCVars.CaptureTimeout));
        _pending[id] = new Pending(ent, user, actor.PlayerSession, targetCoords.MapId, square.Centre, tiles, seen,
            ghosts, deadline);

        RaiseNetworkEvent(new PhotoCaptureRequestEvent
        {
            RequestId = id,
            Map = GetNetEntity(_map.GetMapOrInvalid(targetCoords.MapId)),
            Grid = GetNetEntity(square.Grid),
            Centre = square.Centre,
            Rotation = (float) square.Rotation.Theta,
            Tiles = tiles,
            Visible = visible,
            Monochrome = camera.Monochrome,
            Ghosts = GetNetEntityArray(ghosts.ToArray()),
            Hidden = GetNetEntityArray(hidden.ToArray()),
        }, actor.PlayerSession);

        return true;
    }

    private void OnCaptureResult(PhotoCaptureResultEvent msg, EntitySessionEventArgs args)
    {
        if (!_pending.TryGetValue(msg.RequestId, out var pending) || pending.Session != args.SenderSession)
            return;

        _pending.Remove(msg.RequestId);
        ReleaseGhosts(pending);

        if (TerminatingOrDeleted(pending.Camera) || TerminatingOrDeleted(pending.User)
            || !TryComp<PhotoCameraComponent>(pending.Camera, out var camera))
            return;

        var size = pending.Tiles * PhotoRules.TilePixels;
        if (Clean(msg.Png, size) is not { } png)
        {
            Log.Warning($"{args.SenderSession.Name} sent an unusable photo.");
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-failed"), pending.User, pending.User);
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(png));
        _store.Store(hash, png, size, size, args.SenderSession.UserId);

        var seen = pending.Seen.Where(uid => !TerminatingOrDeleted(uid));
        var caption = _caption.Describe(seen, pending.Tiles, camera.SeeGhosts);
        var description = FormattedMessage.EscapeText(caption.Description);

        if (camera.StealsSouls)
            StealSouls(caption.Revenants);

        _adminLog.Add(LogType.WFPhoto, LogImpact.Low,
            $"{ToPrettyString(pending.User):user} took photo {hash} with {ToPrettyString(pending.Camera):camera} at {new MapCoordinates(pending.Centre, pending.Map)}: {caption.Description}");

        var entry = new PhotoGalleryEntry
        {
            Hash = hash,
            Width = size,
            Height = size,
            Description = description,
            Haunted = caption.Haunted,
        };

        var cameraEnt = (pending.Camera, camera);
        if (camera.StoresPhotos)
        {
            _gallery.Add(pending.Camera, pending.User, entry);
            return;
        }

        Print(cameraEnt, pending.User, pending.Session, entry);
    }

    /// <summary>Uses a shot of film and puts the photo in the photographer's hands.</summary>
    private void Print(Entity<PhotoCameraComponent> ent,
        EntityUid user,
        ICommonSession session,
        PhotoGalleryEntry entry)
    {
        if (ent.Comp.FilmLeft <= 0)
        {
            Popup.PopupEntity(Loc.GetString("wf-photo-camera-no-film"), user, user);
            return;
        }

        ent.Comp.FilmLeft--;
        Dirty(ent);

        var photo = _photo.SpawnPhoto(entry, Transform(user).Coordinates);
        _hands.PickupOrDrop(user, photo);
        Popup.PopupEntity(Loc.GetString("wf-photo-camera-film-left", ("count", ent.Comp.FilmLeft)), user, user);

        if (ent.Comp.Customise)
            _photo.OpenCustomise(photo, session);
    }

    /// <summary>Decodes an upload, checks its size and re-encodes it as a plain PNG.</summary>
    private static byte[]? Clean(byte[]? data, int size)
    {
        if (data == null || data.Length == 0 || data.Length > PhotoRules.MaxPngBytes)
            return null;

        try
        {
            var info = Image.Identify(data);
            if (info.Width != size || info.Height != size)
                return null;

            using var image = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, data);
            using var output = new MemoryStream();
            image.SaveAsPng(output);
            return output.ToArray();
        }
        catch (Exception e) when (e is ImageFormatException or NotSupportedException or InvalidImageContentException)
        {
            return null;
        }
    }

    private void StealSouls(List<EntityUid> revenants)
    {
        foreach (var revenant in revenants)
        {
            if (!TryComp<RevenantComponent>(revenant, out var comp))
                continue;

            _status.TryAddStatusEffect<CorporealComponent>(revenant, "Corporeal", SoulStealDuration, false);
            _stun.TryStun(revenant, SoulStealDuration, false);
            _revenant.ChangeEssenceAmount(revenant, -_random.Next(10, 16), comp);
            Popup.PopupEntity(Loc.GetString("wf-photo-soul-stolen"), revenant, revenant);
        }
    }

    private void ReleaseGhosts(Pending pending)
    {
        foreach (var ghost in pending.Ghosts)
        {
            if (TerminatingOrDeleted(ghost)
                || _pending.Values.Any(p => p.Session == pending.Session && p.Ghosts.Contains(ghost)))
                continue;

            _pvs.RemoveForceSend(ghost, pending.Session);
        }
    }

    /// <summary>The entity a user sees from: a station AI's eye, or the user itself.</summary>
    private EntityUid Viewer(EntityUid user)
    {
        if (TryComp<EyeComponent>(user, out var eye) && eye.Target is { } target && !TerminatingOrDeleted(target))
            return target;

        return user;
    }

    private bool AiCanSee(PhotoSquare square, int index)
    {
        if (!TryComp<MapGridComponent>(square.Grid, out var grid)
            || !TryComp<BroadphaseComponent>(square.Grid, out var broadphase))
            return false;

        return _aiVision.IsAccessible((square.Grid, broadphase, grid), square.GridTile(index));
    }

    /// <summary>Whether an entity is cloaked past the point it can be examined. Photos leave it out.</summary>
    private bool IsCloaked(EntityUid uid)
    {
        return TryComp<StealthComponent>(uid, out var stealth)
            && stealth.Enabled
            && _stealth.GetVisibility(uid, stealth) < stealth.ExamineThreshold;
    }

    private bool IsSpirit(EntityUid uid)
    {
        return HasComp<Shared.Ghost.GhostComponent>(uid) || HasComp<RevenantComponent>(uid);
    }

    /// <summary>Entities in the square's visible tiles.</summary>
    private IEnumerable<EntityUid> InSquare(PhotoSquare square, bool[] visible, MapId mapId)
    {
        var half = square.Tiles / 2f;
        var flags = LookupFlags.Uncontained | LookupFlags.Sundries;
        foreach (var uid in _lookup.GetEntitiesIntersecting(mapId, square.Bounds(), flags))
        {
            var local = (-square.Rotation).RotateVec(_transform.GetWorldPosition(uid) - square.Centre);
            var column = (int) MathF.Floor(local.X + half);
            var row = (int) MathF.Floor(half - local.Y);
            if (column < 0 || row < 0 || column >= square.Tiles || row >= square.Tiles)
                continue;

            if (visible[row * square.Tiles + column])
                yield return uid;
        }
    }
}
