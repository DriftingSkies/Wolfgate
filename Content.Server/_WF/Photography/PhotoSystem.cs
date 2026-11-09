using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._WF.Photography;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WF.Photography;

/// <summary>Printed photos: spawning them, naming them and writing on the back.</summary>
public sealed partial class PhotoSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private PhotoAlbumSystem _albums = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private PhotoStoreSystem _store = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WriteTag = "Write";
    private static readonly EntProtoId PhotoPrototype = "WFPhoto";
    private static readonly SoundSpecifier WriteSound = new SoundCollectionSpecifier("PaperScribbles");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PhotoComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PhotoComponent, GetVerbsEvent<Verb>>(OnVerbs);
    }

    /// <summary>Spawns a printed photo of a stored image.</summary>
    public EntityUid SpawnPhoto(PhotoGalleryEntry entry, EntityCoordinates coordinates)
    {
        var uid = Spawn(PhotoPrototype, coordinates);
        var photo = Comp<PhotoComponent>(uid);
        photo.Hash = entry.Hash;
        photo.Width = entry.Width;
        photo.Height = entry.Height;
        photo.Haunted = entry.Haunted;
        _store.FillThumbnail((uid, photo));
        Dirty(uid, photo);

        if (entry.Name.Length > 0)
            _meta.SetEntityName(uid, entry.Name);
        _meta.SetEntityDescription(uid, entry.Description);
        return uid;
    }

    /// <summary>Asks the photographer for an optional name, description and caption for a new photo.</summary>
    public void OpenCustomise(EntityUid photo, ICommonSession session)
    {
        _quickDialog.OpenDialog(session,
            Loc.GetString("wf-photo-customise-title"),
            Loc.GetString("wf-photo-customise-name"),
            Loc.GetString("wf-photo-customise-description"),
            Loc.GetString("wf-photo-customise-caption"),
            (string name, string description, LongString caption) =>
            {
                if (TerminatingOrDeleted(photo) || session.AttachedEntity is not { } user
                    || !_interaction.InRangeUnobstructed(user, photo))
                    return;

                ApplyCustomise(photo, user, name, description, caption.String);
            });
    }

    private void ApplyCustomise(EntityUid photo, EntityUid user, string name, string description, string caption)
    {
        name = Trim(name, PhotoRules.MaxNameLength);
        description = Trim(description, PhotoRules.MaxDescriptionLength);
        caption = Trim(caption, PhotoRules.MaxBackTextLength);

        if (name.Length > 0)
            _meta.SetEntityName(photo, name);

        if (description.Length > 0)
        {
            var desc = Loc.GetString("wf-photo-customised-description",
                ("custom", FormattedMessage.EscapeText(description)), ("caption", MetaData(photo).EntityDescription));
            _meta.SetEntityDescription(photo, desc);
        }

        if (caption.Length > 0 && TryComp<PhotoComponent>(photo, out var comp))
        {
            comp.BackText = caption;
            Dirty(photo, comp);
        }

        if (name.Length > 0 || description.Length > 0 || caption.Length > 0)
        {
            _albums.PhotoChanged(photo);
            _adminLog.Add(LogType.WFPhoto, LogImpact.Low,
                $"{ToPrettyString(user):user} labelled {ToPrettyString(photo):photo} name '{name}', description '{description}', caption '{caption}'");
        }
    }

    private void OnExamined(Entity<PhotoComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
        {
            args.PushMarkup(Loc.GetString("wf-photo-examine-closer"));
            return;
        }

        if (ent.Comp.BackText.Length > 0)
        {
            var back = FormattedMessage.EscapeText(ent.Comp.BackText);
            args.PushMarkup(Loc.GetString("wf-photo-examine-back", ("text", back)));
        }
    }

    private void OnInteractUsing(Entity<PhotoComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tag.HasTag(args.Used, WriteTag) || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        args.Handled = true;
        var user = args.User;
        var pen = args.Used;
        _quickDialog.OpenDialog(actor.PlayerSession,
            Loc.GetString("wf-photo-write-title"),
            Loc.GetString("wf-photo-write-prompt"),
            (LongString text) =>
            {
                if (TerminatingOrDeleted(ent) || !_interaction.InRangeUnobstructed(user, ent.Owner))
                    return;

                var back = Trim(text.String, PhotoRules.MaxBackTextLength);
                if (back.Length == 0)
                    return;

                ent.Comp.BackText = back;
                Dirty(ent);
                _albums.PhotoChanged(ent);
                _audio.PlayPvs(WriteSound, ent);
                _popup.PopupEntity(Loc.GetString("wf-photo-write-done"), ent, user);
                _adminLog.Add(LogType.WFPhoto, LogImpact.Low,
                    $"{ToPrettyString(user):user} wrote '{back}' on the back of {ToPrettyString(ent):photo} with {ToPrettyString(pen):pen}");
            });
    }

    private void OnVerbs(Entity<PhotoComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        var user = args.User;
        var session = actor.PlayerSession;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-photo-verb-rename"),
            Act = () =>
            {
                _quickDialog.OpenDialog(session,
                    Loc.GetString("wf-photo-rename-title"),
                    Loc.GetString("wf-photo-rename-prompt"),
                    (string name) =>
                    {
                        if (TerminatingOrDeleted(ent) || !_interaction.InRangeUnobstructed(user, ent.Owner))
                            return;

                        name = Trim(name, PhotoRules.MaxNameLength);
                        _meta.SetEntityName(ent, name.Length > 0
                            ? Loc.GetString("wf-photo-renamed", ("name", name))
                            : Loc.GetString("wf-photo-default-name"));
                        _albums.PhotoChanged(ent);
                        _adminLog.Add(LogType.WFPhoto, LogImpact.Low,
                            $"{ToPrettyString(user):user} renamed {ToPrettyString(ent):photo} to '{name}'");
                    });
            },
        });
    }

    private static string Trim(string text, int max)
    {
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }
}
