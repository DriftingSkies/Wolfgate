using System.Numerics;
using Content.Shared._WF.Photography;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Photography;

/// <summary>Lists a station AI's or cyborg's stored photos and shows the chosen one.</summary>
public sealed partial class PhotoGalleryWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entMan = default!;

    private const float ImageSide = 320f;

    private readonly PhotoImageSystem _images;
    private readonly ItemList _list;
    private readonly TextureRect _image;
    private readonly RichTextLabel _description;
    private readonly Button _print;
    private readonly Button _delete;
    private readonly List<string> _shownNames = new();
    private bool _shown;
    private int _selected = -1;

    /// <summary>The entity holding the gallery.</summary>
    public EntityUid Target;

    /// <summary>Raised with the index of the photo to print.</summary>
    public Action<int>? OnPrint;

    /// <summary>Raised with the index of the photo to forget.</summary>
    public Action<int>? OnDelete;

    public PhotoGalleryWindow()
    {
        IoCManager.InjectDependencies(this);
        _images = _entMan.System<PhotoImageSystem>();

        Title = Loc.GetString("wf-photo-gallery-title");
        MinSize = new Vector2(ImageSide + 260, ImageSide + 80);

        _list = new ItemList
        {
            MinSize = new Vector2(220, 0),
            VerticalExpand = true,
            SelectMode = ItemList.ItemListSelectMode.Single,
        };
        _list.OnItemSelected += args => Select(args.ItemIndex);

        _image = new TextureRect
        {
            MinSize = new Vector2(ImageSide, ImageSide),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        };
        _description = new RichTextLabel { HorizontalExpand = true, MaxWidth = ImageSide };

        _print = new Button { Text = Loc.GetString("wf-photo-gallery-print"), Disabled = true };
        _print.OnPressed += _ => OnPrint?.Invoke(_selected);
        _delete = new Button { Text = Loc.GetString("wf-photo-gallery-delete"), Disabled = true };
        _delete.OnPressed += _ => OnDelete?.Invoke(_selected);

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };
        buttons.AddChild(_print);
        buttons.AddChild(_delete);

        var right = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        right.AddChild(_image);
        right.AddChild(_description);
        right.AddChild(buttons);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        row.AddChild(_list);
        row.AddChild(right);
        Contents.AddChild(row);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_entMan.TryGetComponent<PhotoGalleryComponent>(Target, out var gallery))
            return;

        if (!_shown || !SameEntries(gallery))
            Rebuild(gallery);

        if (_selected >= 0 && _selected < gallery.Photos.Count)
            _image.Texture = _images.Get(gallery.Photos[_selected].Hash);
    }

    /// <summary>Whether the list still shows the gallery's entries. Entry names are unique, so they stand for them.</summary>
    private bool SameEntries(PhotoGalleryComponent gallery)
    {
        if (gallery.Photos.Count != _shownNames.Count)
            return false;

        for (var i = 0; i < _shownNames.Count; i++)
        {
            if (gallery.Photos[i].Name != _shownNames[i])
                return false;
        }

        return true;
    }

    private void Rebuild(PhotoGalleryComponent gallery)
    {
        _shown = true;
        _shownNames.Clear();
        _list.Clear();
        foreach (var photo in gallery.Photos)
        {
            _shownNames.Add(photo.Name);
            _list.AddItem(photo.Name).TooltipText = photo.Name;
        }

        _print.Visible = gallery.PrintCharge != null;
        Select(gallery.Photos.Count == 0 ? -1 : Math.Clamp(_selected, 0, gallery.Photos.Count - 1));
    }

    private void Select(int index)
    {
        _selected = index;
        var has = _entMan.TryGetComponent<PhotoGalleryComponent>(Target, out var gallery)
            && index >= 0 && index < gallery.Photos.Count;

        _print.Disabled = !has;
        _delete.Disabled = !has;
        _image.Texture = null;
        var description = has ? gallery!.Photos[index].Description : string.Empty;
        _description.SetMessage(FormattedMessage.FromMarkupPermissive(description));

        if (has && !_list[index].Selected)
            _list[index].Selected = true;
    }
}

/// <summary>Opens the gallery window and sends its print and delete buttons.</summary>
public sealed class PhotoGalleryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PhotoGalleryWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PhotoGalleryWindow>();
        _window.Target = Owner;
        _window.OnPrint += index => SendMessage(new PhotoGalleryPrintMessage(index));
        _window.OnDelete += index => SendMessage(new PhotoGalleryDeleteMessage(index));
    }
}
