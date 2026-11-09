using System.Numerics;
using Content.Shared._WF.Photography;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Photography;

/// <summary>Shows a photo at full size with its description and the writing on its back.</summary>
public sealed partial class PhotoWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entMan = default!;

    /// <summary>Longest side the image is drawn at, in pixels.</summary>
    private const float ImageSide = 480f;

    private readonly PhotoImageSystem _images;
    private readonly TextureRect _image;
    private readonly Label _waiting;
    private readonly RichTextLabel _description;
    private readonly RichTextLabel _back;
    private string _shownDescription = string.Empty;
    private string _shownBack = string.Empty;

    /// <summary>The photo shown, or a frame holding one.</summary>
    public EntityUid Target;

    public PhotoWindow()
    {
        IoCManager.InjectDependencies(this);
        _images = _entMan.System<PhotoImageSystem>();

        SetSize = new Vector2(ImageSide + 20, ImageSide + 140);
        MinSize = new Vector2(ImageSide + 20, 0);

        _image = new TextureRect
        {
            MinSize = new Vector2(ImageSide, ImageSide),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            HorizontalAlignment = HAlignment.Center,
            CanShrink = true,
        };
        _waiting = new Label
        {
            Text = Loc.GetString("wf-photo-window-loading"),
            HorizontalAlignment = HAlignment.Center,
        };
        _description = new RichTextLabel { HorizontalExpand = true, MaxWidth = ImageSide };
        _back = new RichTextLabel { HorizontalExpand = true, MaxWidth = ImageSide };

        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        text.AddChild(_description);
        text.AddChild(_back);

        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        scroll.AddChild(text);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        box.AddChild(_image);
        box.AddChild(_waiting);
        box.AddChild(scroll);
        Contents.AddChild(box);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        Refresh();
    }

    private void Refresh()
    {
        var photoUid = ResolvePhoto();
        if (photoUid == null || !_entMan.TryGetComponent<PhotoComponent>(photoUid, out var photo))
        {
            Title = Loc.GetString("wf-photo-window-empty-title");
            _image.Texture = null;
            _waiting.Text = Loc.GetString("wf-photo-window-empty");
            _waiting.Visible = true;
            _description.Visible = false;
            _back.Visible = false;
            return;
        }

        var meta = _entMan.GetComponent<MetaDataComponent>(photoUid.Value);
        Title = meta.EntityName;

        _image.Texture = _images.Get(photo.Hash);
        _waiting.Text = Loc.GetString(string.IsNullOrEmpty(photo.Hash) ?"wf-photo-window-blank" : "wf-photo-window-loading");
        _waiting.Visible = _image.Texture == null;

        if (_shownDescription != meta.EntityDescription)
        {
            _shownDescription = meta.EntityDescription;
            _description.SetMessage(FormattedMessage.FromMarkupPermissive(_shownDescription));
        }

        if (_shownBack != photo.BackText)
        {
            _shownBack = photo.BackText;
            var back = Loc.GetString("wf-photo-window-back", ("text", _shownBack));
            _back.SetMessage(FormattedMessage.FromUnformatted(back));
        }

        _description.Visible = _shownDescription.Length > 0;
        _back.Visible = _shownBack.Length > 0;
    }

    private EntityUid? ResolvePhoto()
    {
        if (_entMan.HasComponent<PhotoComponent>(Target))
            return Target;

        var containers = _entMan.System<SharedContainerSystem>();
        if (_entMan.HasComponent<PhotoFrameComponent>(Target)
            && containers.TryGetContainer(Target, PhotoFrameComponent.ContainerId, out var container)
            && container.ContainedEntities.Count > 0)
        {
            return container.ContainedEntities[0];
        }

        return null;
    }
}

/// <summary>Opens the photo window for a photo or a frame.</summary>
public sealed class PhotoBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PhotoWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PhotoWindow>();
        _window.Target = Owner;
    }
}
