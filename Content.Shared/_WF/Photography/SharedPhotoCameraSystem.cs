using System.Numerics;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Photography;

/// <summary>Camera settings verbs, examine text and the ready sprite, predicted on both sides.</summary>
public abstract partial class SharedPhotoCameraSystem : EntitySystem
{
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] protected SharedAudioSystem Audio = default!;
    [Dependency] protected SharedPopupSystem Popup = default!;
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly VerbCategory SettingsCategory = new("wf-photo-camera-verb-settings", null);

    private static readonly VerbCategory ZoomCategory =
        new("wf-photo-camera-verb-zoom", null) { Parent = SettingsCategory };

    private static readonly VerbCategory ColourCategory =
        new("wf-photo-camera-verb-colour", null) { Parent = SettingsCategory };

    private static readonly VerbCategory FlashCategory =
        new("wf-photo-camera-verb-flash", null) { Parent = SettingsCategory };

    private static readonly VerbCategory LabelsCategory =
        new("wf-photo-camera-verb-labels", null) { Parent = SettingsCategory };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoCameraComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PhotoCameraComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerbs);
        SubscribeLocalEvent<PhotoCameraComponent, GetVerbsEvent<Verb>>(OnVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = Timing.CurTime;
        var query = EntityQueryEnumerator<PhotoCameraComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out var camera, out var appearance))
        {
            var ready = now >= camera.NextShot;
            if (!Appearance.TryGetData<bool>(uid, PhotoCameraVisuals.Ready, out var shown, appearance)
                || shown != ready)
                Appearance.SetData(uid, PhotoCameraVisuals.Ready, ready, appearance);
        }
    }

    private void OnExamined(Entity<PhotoCameraComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(PhotoCameraComponent)))
        {
            if (!ent.Comp.StoresPhotos)
                args.PushMarkup(Loc.GetString("wf-photo-camera-examine-film", ("count", ent.Comp.FilmLeft)));

            var tiles = PhotoRules.TilesForAperture(ent.Comp.Aperture);
            args.PushMarkup(Loc.GetString("wf-photo-camera-examine-size", ("size", tiles)));
            args.PushMarkup(Loc.GetString(ent.Comp.Monochrome
                ? "wf-photo-camera-examine-monochrome"
                : "wf-photo-camera-examine-colour"));
        }
    }

    private void OnAltVerbs(Entity<PhotoCameraComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-photo-camera-verb-next-size"),
            Category = ZoomCategory,
            Priority = 1,
            Act = () => SetAperture(ent, user, PhotoRules.NextAperture(ent.Comp.Aperture)),
        });
    }

    private void OnVerbs(Entity<PhotoCameraComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        for (var aperture = PhotoRules.MinAperture; aperture <= PhotoRules.MaxAperture; aperture++)
        {
            var value = aperture;
            var tiles = PhotoRules.TilesForAperture(value);
            args.Verbs.Add(Choice(ZoomCategory,
                Loc.GetString("wf-photo-camera-verb-size-option", ("size", tiles)),
                ent.Comp.Aperture == value,
                PhotoRules.MaxAperture - value,
                () => SetAperture(ent, user, value)));
        }

        args.Verbs.Add(Choice(ColourCategory, Loc.GetString("wf-photo-camera-verb-colour-option"),
            !ent.Comp.Monochrome, 1, () => SetMonochrome(ent, user, false)));
        args.Verbs.Add(Choice(ColourCategory, Loc.GetString("wf-photo-camera-verb-monochrome-option"),
            ent.Comp.Monochrome, 0, () => SetMonochrome(ent, user, true)));

        args.Verbs.Add(Choice(FlashCategory, Loc.GetString("wf-photo-camera-verb-on"),
            ent.Comp.Flash, 1, () => SetFlash(ent, user, true)));
        args.Verbs.Add(Choice(FlashCategory, Loc.GetString("wf-photo-camera-verb-off"),
            !ent.Comp.Flash, 0, () => SetFlash(ent, user, false)));

        if (ent.Comp.StoresPhotos)
            return;

        args.Verbs.Add(Choice(LabelsCategory, Loc.GetString("wf-photo-camera-verb-on"),
            ent.Comp.Customise, 1, () => SetCustomise(ent, user, true)));
        args.Verbs.Add(Choice(LabelsCategory, Loc.GetString("wf-photo-camera-verb-off"),
            !ent.Comp.Customise, 0, () => SetCustomise(ent, user, false)));
    }

    /// <summary>A settings choice, disabled when it is the current setting.</summary>
    private Verb Choice(VerbCategory category, string text, bool current, int priority, Action act)
    {
        return new Verb
        {
            Text = text,
            Category = category,
            Priority = priority,
            Disabled = current,
            Message = current ? Loc.GetString("wf-photo-camera-verb-current") : null,
            Act = act,
        };
    }

    private void SetAperture(Entity<PhotoCameraComponent> ent, EntityUid user, int aperture)
    {
        ent.Comp.Aperture = aperture;
        Dirty(ent);
        var tiles = PhotoRules.TilesForAperture(aperture);
        Popup.PopupClient(Loc.GetString("wf-photo-camera-size-set", ("size", tiles)), ent, user);
        PlaySetting(ent, user);
    }

    private void SetMonochrome(Entity<PhotoCameraComponent> ent, EntityUid user, bool monochrome)
    {
        ent.Comp.Monochrome = monochrome;
        Dirty(ent);
        Popup.PopupClient(Loc.GetString(monochrome
            ? "wf-photo-camera-monochrome-on"
            : "wf-photo-camera-monochrome-off"), ent, user);
        PlaySetting(ent, user);
    }

    private void SetFlash(Entity<PhotoCameraComponent> ent, EntityUid user, bool flash)
    {
        ent.Comp.Flash = flash;
        Dirty(ent);
        PlaySetting(ent, user);
    }

    private void SetCustomise(Entity<PhotoCameraComponent> ent, EntityUid user, bool customise)
    {
        ent.Comp.Customise = customise;
        Dirty(ent);
        PlaySetting(ent, user);
    }

    /// <summary>The square of tiles around a point, lined up with the grid under it.</summary>
    public PhotoSquare GetSquare(MapCoordinates target, int tiles)
    {
        if (_map.TryFindGridAt(target, out var gridUid, out var grid))
        {
            var tile = _map.TileIndicesFor(gridUid, grid, target);
            var centre = _map.GridTileToWorldPos(gridUid, grid, tile);
            return new PhotoSquare(gridUid, centre, _transform.GetWorldRotation(gridUid), tiles, tile);
        }

        var snapped = new Vector2(MathF.Floor(target.Position.X) + 0.5f, MathF.Floor(target.Position.Y) + 0.5f);
        return new PhotoSquare(EntityUid.Invalid, snapped, Angle.Zero, tiles);
    }

    /// <summary>Plays a camera sound, only to the user when the camera is silent.</summary>
    protected void PlayCameraSound(Entity<PhotoCameraComponent> ent, SoundSpecifier sound, EntityUid? user,
        bool predicted)
    {
        if (ent.Comp.Silent && user != null)
        {
            if (predicted)
                Audio.PlayLocal(sound, ent, user);
            else
                Audio.PlayEntity(sound, user.Value, ent);
            return;
        }

        if (predicted)
            Audio.PlayPredicted(sound, ent, user);
        else
            Audio.PlayPvs(sound, ent);
    }

    private void PlaySetting(Entity<PhotoCameraComponent> ent, EntityUid user)
    {
        PlayCameraSound(ent, ent.Comp.ClickSound, user, predicted: true);
    }
}
