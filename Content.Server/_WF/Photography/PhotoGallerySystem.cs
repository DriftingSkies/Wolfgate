using Content.Server.PowerCell;
using Content.Shared._WF.Photography;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Photography;

/// <summary>Stored photos of station AIs and cyborg cameras: adding, printing and forgetting them.</summary>
public sealed partial class PhotoGallerySystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PhotoSystem _photo = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    /// <summary>Farthest a printed photo lands from the printer, in tiles.</summary>
    private const float PrintScatter = 0.3f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoGalleryComponent, PhotoGalleryActionEvent>(OnGalleryAction);
        SubscribeLocalEvent<PhotoGalleryComponent, PhotoGalleryPrintMessage>(OnPrint);
        SubscribeLocalEvent<PhotoGalleryComponent, PhotoGalleryDeleteMessage>(OnDelete);
    }

    /// <summary>Keeps a new photo, dropping the oldest when the gallery is full.</summary>
    public void Add(EntityUid gallery, EntityUid user, PhotoGalleryEntry entry)
    {
        if (!TryComp<PhotoGalleryComponent>(gallery, out var comp))
            return;

        entry.Name = Loc.GetString("wf-photo-gallery-entry-name",
            ("number", comp.NextNumber++), ("user", user));
        comp.Photos.Add(entry);
        while (comp.Photos.Count > comp.Capacity)
        {
            comp.Photos.RemoveAt(0);
        }

        Dirty(gallery, comp);
        _popup.PopupEntity(Loc.GetString("wf-photo-gallery-recorded"), user, user);
    }

    private void OnGalleryAction(Entity<PhotoGalleryComponent> ent, ref PhotoGalleryActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_ui.HasUi(ent.Owner, PhotoUiKey.Gallery))
            _ui.SetUi(ent.Owner, PhotoUiKey.Gallery, new InterfaceData("PhotoGalleryBoundUserInterface"));

        _ui.TryOpenUi(ent.Owner, PhotoUiKey.Gallery, args.Performer);
    }

    private void OnPrint(Entity<PhotoGalleryComponent> ent, ref PhotoGalleryPrintMessage args)
    {
        if (ent.Comp.PrintCharge is not { } charge || args.Index < 0 || args.Index >= ent.Comp.Photos.Count)
            return;

        var user = args.Actor;
        if (!_powerCell.TryUseCharge(user, charge, user: user))
        {
            _popup.PopupEntity(Loc.GetString("wf-photo-gallery-no-charge"), user, user);
            return;
        }

        var coords = _transform.GetMoverCoordinates(user)
            .Offset(_random.NextVector2(PrintScatter));
        _photo.SpawnPhoto(ent.Comp.Photos[args.Index], coords);
        _audio.PlayPvs(ent.Comp.PrintSound, user);
        _popup.PopupEntity(Loc.GetString("wf-photo-gallery-printed", ("user", user)), user);
    }

    private void OnDelete(Entity<PhotoGalleryComponent> ent, ref PhotoGalleryDeleteMessage args)
    {
        if (args.Index < 0 || args.Index >= ent.Comp.Photos.Count)
            return;

        ent.Comp.Photos.RemoveAt(args.Index);
        Dirty(ent);
    }
}
