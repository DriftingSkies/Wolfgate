using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.Paintings;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WG.Paintings;

/// <summary>Searches every stored painting by its values, and blocks or deletes them.</summary>
public sealed class WGPaintingAdminWindow : DefaultWindow
{
    private static readonly Color Dim = Color.FromHex("#9aa7b1");
    private static readonly Color Warning = Color.FromHex("#d08080");

    private static readonly TextureLoadParameters Parameters =
        TextureLoadParameters.Default with { SampleParameters = TextureSampleParameters.Default };

    public event Action<string, WGPaintingAdminFilter>? OnSearch;
    public event Action<string>? OnSelect;
    public event Action<string, bool>? OnBlock;
    public event Action<string>? OnDelete;

    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly OptionButton _filter = new();
    private readonly Label _count = new() { FontColorOverride = Dim, Margin = new Thickness(8, 0, 0, 0) };
    private readonly BoxContainer _results = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _details = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly TextureRect _preview = new()
    {
        Stretch = TextureRect.StretchMode.KeepAspectCentered,
        MinSize = new Vector2(320, 192),
    };
    private readonly Label _previewNote = new() { FontColorOverride = Dim, HorizontalAlignment = HAlignment.Center };
    private readonly Button _block = new();
    private ConfirmButton _delete;
    private readonly BoxContainer _actions = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };

    private WGPaintingAdminEntry? _selected;
    private string? _previewHash;
    private Texture? _texture;

    public WGPaintingAdminWindow()
    {
        Title = Loc.GetString("wg-painting-admin-title");
        SetSize = new Vector2(1000, 600);

        _search.PlaceHolder = Loc.GetString("wg-painting-admin-search");
        _search.OnTextChanged += _ => Search();
        _filter.AddItem(Loc.GetString("wg-painting-admin-filter-all"), (int) WGPaintingAdminFilter.All);
        _filter.AddItem(Loc.GetString("wg-painting-admin-filter-visible"), (int) WGPaintingAdminFilter.Visible);
        _filter.AddItem(Loc.GetString("wg-painting-admin-filter-blocked"), (int) WGPaintingAdminFilter.Blocked);
        _filter.OnItemSelected += args =>
        {
            _filter.SelectId(args.Id);
            Search();
        };

        _block.OnPressed += _ =>
        {
            if (_selected is { } entry)
                OnBlock?.Invoke(entry.Hash, !entry.Blocked);
        };
        _delete = DeleteButton();
        _actions.AddChild(_block);
        _actions.AddChild(_delete);

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        top.AddChild(_search);
        top.AddChild(_filter);
        top.AddChild(_count);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
        var list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        list.AddChild(Header());
        list.AddChild(_results);
        scroll.AddChild(list);

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 340,
            MaxWidth = 340,
            SeparationOverride = 6,
            Margin = new Thickness(8, 0, 0, 0),
        };
        side.AddChild(_preview);
        side.AddChild(_previewNote);
        var detailScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        detailScroll.AddChild(_details);
        side.AddChild(detailScroll);
        side.AddChild(_actions);

        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true };
        body.AddChild(scroll);
        body.AddChild(side);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        root.AddChild(top);
        root.AddChild(new Label
        {
            Text = Loc.GetString("wg-painting-admin-help"),
            FontColorOverride = Dim,
            StyleClasses = { "LabelSmall" },
        });
        root.AddChild(body);
        Contents.AddChild(root);
        ShowSelected(null, null);
    }

    private ConfirmButton DeleteButton()
    {
        var button = new ConfirmButton
        {
            Text = Loc.GetString("wg-painting-admin-delete"),
            ConfirmationText = Loc.GetString("wg-painting-admin-delete-confirm"),
        };
        button.OnPressed += _ =>
        {
            if (_selected is { } entry)
                OnDelete?.Invoke(entry.Hash);
        };
        return button;
    }

    private void Search()
    {
        OnSearch?.Invoke(_search.Text, (WGPaintingAdminFilter) _filter.SelectedId);
    }

    private static readonly int[] Widths = { 190, 130, 110, 60, 80 };

    private static BoxContainer Columns(params string[] texts)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        for (var i = 0; i < texts.Length; i++)
        {
            var label = new Label { Text = texts[i], ClipText = true, Margin = new Thickness(0, 0, 6, 0) };
            if (i < Widths.Length)
            {
                label.MinWidth = Widths[i];
                label.MaxWidth = Widths[i];
            }
            else
            {
                label.HorizontalExpand = true;
            }

            row.AddChild(label);
        }

        return row;
    }

    private static Control Header()
    {
        var header = Columns(
            Loc.GetString("wg-painting-admin-column-title"),
            Loc.GetString("wg-painting-admin-column-artist"),
            Loc.GetString("wg-painting-admin-column-author"),
            Loc.GetString("wg-painting-admin-column-size"),
            Loc.GetString("wg-painting-admin-column-date"),
            Loc.GetString("wg-painting-admin-column-status"));
        header.Margin = new Thickness(6, 0, 6, 2);
        foreach (var child in header.Children)
            ((Label) child).FontColorOverride = Dim;
        return header;
    }

    public void UpdateState(WGPaintingAdminEuiState state)
    {
        if (!_search.HasKeyboardFocus() && _search.Text != state.Query)
            _search.Text = state.Query;
        _filter.SelectId((int) state.Filter);
        _count.Text = !state.Loaded
            ? Loc.GetString("wg-painting-admin-loading")
            : Loc.GetString("wg-painting-admin-count", ("shown", state.Results.Count), ("total", state.Total));

        _results.RemoveAllChildren();
        foreach (var entry in state.Results)
            _results.AddChild(Row(entry, entry.Hash == state.Selected?.Hash));
        if (state.Loaded && state.Results.Count == 0)
            _results.AddChild(new Label { Text = Loc.GetString("wg-painting-admin-none"), FontColorOverride = Dim });

        ShowSelected(state.Selected, state.Pixels);
    }

    private Control Row(WGPaintingAdminEntry entry, bool selected)
    {
        var button = new ContainerButton
        {
            ToggleMode = true,
            Pressed = selected,
            HorizontalExpand = true,
            StyleClasses = { ContainerButton.StyleClassButton, "ButtonSquare" },
        };
        var row = Columns(
            entry.Title,
            entry.Artist,
            Player(entry.AuthorName, entry.Author),
            $"{entry.Width}x{entry.Height}",
            entry.CreatedAt.ToString("yyyy-MM-dd"),
            Loc.GetString(entry.Blocked ? "wg-painting-admin-status-blocked" : "wg-painting-admin-status-visible"));
        row.Margin = new Thickness(6, 2);
        if (entry.Blocked)
            ((Label) row.GetChild(row.ChildCount - 1)).FontColorOverride = Warning;
        button.AddChild(row);
        button.OnPressed += _ =>
        {
            button.Pressed = true;
            OnSelect?.Invoke(entry.Hash);
        };
        return button;
    }

    private static string Player(string name, Guid? id)
    {
        if (id == null)
            return Loc.GetString("wg-painting-admin-no-player");
        return name.Length > 0 ? name : id.Value.ToString()[..8];
    }

    private void ShowSelected(WGPaintingAdminEntry? entry, byte[]? pixels)
    {
        // A delete armed for one painting must not confirm another; the button has no reset, so it is replaced.
        if (_delete.IsConfirming && entry?.Hash != _selected?.Hash)
        {
            _actions.RemoveChild(_delete);
            _delete = DeleteButton();
            _actions.AddChild(_delete);
        }

        _selected = entry;
        _details.RemoveAllChildren();
        _actions.Visible = entry != null;
        if (entry == null)
        {
            SetPreview(null, null);
            _previewNote.Text = Loc.GetString("wg-painting-admin-select");
            return;
        }

        if (entry.Hash != _previewHash || pixels != null && _texture == null)
            SetPreview(entry, pixels);
        _previewNote.Text = _texture == null ? Loc.GetString("wg-painting-admin-preview-loading") : string.Empty;

        Detail("wg-painting-admin-field-title", entry.Title);
        Detail("wg-painting-admin-field-artist", entry.Artist);
        Detail("wg-painting-admin-field-author", entry.Author == null
            ? Loc.GetString("wg-painting-admin-no-player")
            : $"{entry.AuthorName} {entry.Author}".Trim());
        Detail("wg-painting-admin-field-scanner", $"{entry.ScannerName} {entry.ScannedBy}".Trim());
        Detail("wg-painting-admin-field-size", $"{entry.Width}x{entry.Height}");
        Detail("wg-painting-admin-field-date", entry.CreatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'"));
        Detail("wg-painting-admin-field-hash", entry.Hash);
        Detail("wg-painting-admin-field-status",
            Loc.GetString(entry.Blocked ? "wg-painting-admin-status-blocked" : "wg-painting-admin-status-visible"));

        _block.Text = Loc.GetString(entry.Blocked ? "wg-painting-admin-unblock" : "wg-painting-admin-block");
    }

    /// <summary>A label and its value in a read-only box, so hashes and ids can be copied.</summary>
    private void Detail(string label, string value)
    {
        _details.AddChild(new Label { Text = Loc.GetString(label), FontColorOverride = Dim, StyleClasses = { "LabelSmall" } });
        _details.AddChild(new LineEdit { Text = value, Editable = false, HorizontalExpand = true });
    }

    private void SetPreview(WGPaintingAdminEntry? entry, byte[]? pixels)
    {
        DisposePreview();
        _previewHash = entry?.Hash;
        if (entry == null || pixels == null || pixels.Length != entry.Width * entry.Height * 4)
            return;

        using var image = new Image<Rgba32>(entry.Width, entry.Height);
        for (var y = 0; y < entry.Height; y++)
        {
            for (var x = 0; x < entry.Width; x++)
            {
                var i = (y * entry.Width + x) * 4;
                image[x, y] = new Rgba32(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
            }
        }

        _texture = Texture.LoadFromImage(image, "painting", Parameters);
        _preview.Texture = _texture;
    }

    public void DisposePreview()
    {
        _preview.Texture = null;
        (_texture as OwnedTexture)?.Dispose();
        _texture = null;
    }
}
