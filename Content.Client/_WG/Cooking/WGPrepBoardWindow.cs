using System.Numerics;
using Content.Shared._WG.Cooking;
using System.Linq;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WG.Cooking;

/// <summary>
/// The prep board window: what lies on the board down the left, each with Chop and Take; the dishes it is working
/// toward down the right, closest first, with what is there and what is missing; and Put together. Chop opens the
/// chopping minigame over the ingredient.
/// </summary>
public sealed class WGPrepBoardWindow : DefaultWindow
{
    private static readonly Color ReadyColor = Color.FromHex("#6FCF6F");
    private static readonly Color MissingColor = Color.FromHex("#E0B04F");

    public event Action? OnAssemble;
    public event Action<NetEntity>? OnTake;
    public event Action<NetEntity, float, int>? OnChop;

    private readonly BoxContainer _items;
    private readonly BoxContainer _recipes;
    private readonly Button _assemble;
    private readonly BoxContainer _main;
    private readonly BoxContainer _chopPanel;
    private readonly WGChopMinigame _minigame;
    private readonly Label _chopTitle;
    private readonly Label _chopFeedback;
    private NetEntity _chopping;
    private int _option;

    public WGPrepBoardWindow()
    {
        Title = Loc.GetString("wg-prep-ui-title");
        MinSize = new Vector2(620, 420);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6) };
        _main = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10, VerticalExpand = true };

        var left = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
        left.AddChild(new Label { Text = Loc.GetString("wg-prep-ui-on-board"), StyleClasses = { "LabelHeading" } });
        _items = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        left.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _items } });
        _main.AddChild(left);

        var right = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
        right.AddChild(new Label { Text = Loc.GetString("wg-prep-ui-recipes"), StyleClasses = { "LabelHeading" } });
        _recipes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        right.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _recipes } });
        _assemble = new Button { Text = Loc.GetString("wg-prep-ui-assemble"), StyleClasses = { "OpenBoth" } };
        _assemble.OnPressed += _ => OnAssemble?.Invoke();
        right.AddChild(_assemble);
        _main.AddChild(right);
        root.AddChild(_main);

        // The chopping view, shown in place of the lists while chopping.
        _chopPanel = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Visible = false, VerticalExpand = true, SeparationOverride = 4 };
        _chopTitle = new Label { StyleClasses = { "LabelHeading" } };
        _chopPanel.AddChild(_chopTitle);
        _chopPanel.AddChild(new Label { Text = Loc.GetString("wg-chop-hint"), StyleClasses = { "LabelSubText" } });
        _minigame = new WGChopMinigame { HorizontalExpand = true, VerticalExpand = true };
        _chopPanel.AddChild(_minigame);
        _chopFeedback = new Label();
        _chopPanel.AddChild(_chopFeedback);
        var back = new Button { Text = Loc.GetString("wg-chop-stop") };
        back.OnPressed += _ => ShowLists();
        _chopPanel.AddChild(back);
        root.AddChild(_chopPanel);
        _minigame.OnStroke += text => _chopFeedback.Text = text;
        _minigame.OnFinished += score =>
        {
            ShowLists();
            OnChop?.Invoke(_chopping, score, _option);
        };

        Contents.AddChild(root);
    }

    private void ShowLists()
    {
        _chopPanel.Visible = false;
        _main.Visible = true;
    }

    /// <summary>Whether the chopping minigame is showing.</summary>
    public bool Chopping => _chopPanel.Visible;

    /// <summary>Opens the chopping minigame over one thing on the board, to cut it the <paramref name="option"/>th way.</summary>
    public void StartChop(WGPrepItemView item, IEntityManager entMan, int option = 0)
    {
        try
        {
            _chopping = item.Entity;
            _option = option;
            _chopTitle.Text = option < item.Options.Count
                ? Loc.GetString("wg-chop-title-into", ("item", item.Name), ("result", item.Options[option]))
                : Loc.GetString("wg-chop-title", ("item", item.Name));
            _chopFeedback.Text = string.Empty;
            // The minigame draws the ingredient itself, so it can cut it into pieces.
            Texture? texture = null;
            if (item.Prototype != null && IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>().HasIndex<Robust.Shared.Prototypes.EntityPrototype>(item.Prototype))
                texture = entMan.System<SpriteSystem>().GetPrototypeIcon(item.Prototype).Default;
            var bounds = entMan.TryGetEntity(item.Entity, out var uid) ? SpriteBounds(uid.Value, entMan) : null;
            _minigame.Start(texture, bounds, option < item.OptionCuts.Count ? item.OptionCuts[option] : item.Cuts);
            _main.Visible = false;
            _chopPanel.Visible = true;
        }
        catch (Exception e)
        {
            // Never take the client down over the minigame; chop without it.
            Logger.GetSawmill("wg.cooking").Error($"Couldn't open the chopping minigame: {e}");
            ShowLists();
        }
    }

    /// <summary>Visible shapes already worked out, by sprite file and state.</summary>
    private static readonly Dictionary<string, UIBox2?> BoundsCache = new();

    /// <summary>
    /// Where the item's sprite has visible pixels, as fractions of a frame, read from its PNG file once and cached.
    /// Reading the loaded texture pixel by pixel instead copies a whole texture atlas off the GPU for every pixel.
    /// </summary>
    private static UIBox2? SpriteBounds(EntityUid uid, IEntityManager entMan)
    {
        // The first layer's own RSI, which may differ from the sprite's base one.
        if (!entMan.TryGetComponent<SpriteComponent>(uid, out var sprite) || sprite.AllLayers.FirstOrDefault() is not { } layer
            || layer.ActualRsi is not { } rsi || layer.RsiState.Name is not { } state)
            return null;

        var file = rsi.Path / $"{state}.png";
        var key = file.ToString();
        if (BoundsCache.TryGetValue(key, out var cached))
            return cached;

        UIBox2? bounds = null;
        try
        {
            using var stream = IoCManager.Resolve<IResourceCache>().ContentFileRead(file);
            using var image = Image.Load<Rgba32>(stream);
            var width = Math.Min(rsi.Size.X, image.Width);
            var height = Math.Min(rsi.Size.Y, image.Height);
            // The sandbox lets content read an image's pixels only through its span.
            var pixels = image.GetPixelSpan();
            int left = width, top = height, right = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * image.Width + x].A < 26)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }

            if (right >= left)
                bounds = new UIBox2(left / (float) width, top / (float) height, (right + 1) / (float) width, (bottom + 1) / (float) height);
        }
        catch (Exception)
        {
            bounds = null;
        }

        BoundsCache[key] = bounds;
        return bounds;
    }

    public void Update(WGPrepBoardState state, IEntityManager entMan)
    {
        _items.RemoveAllChildren();
        if (state.Items.Count == 0)
            _items.AddChild(new Label { Text = Loc.GetString("wg-prep-ui-empty"), StyleClasses = { "LabelSubText" } });

        foreach (var item in state.Items)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            var view = new SpriteView { SetSize = new Vector2(32, 32) };
            if (entMan.TryGetEntity(item.Entity, out var uid))
                view.SetEntity(uid);
            row.AddChild(view);
            row.AddChild(new Label { Text = item.Name, HorizontalExpand = true, ClipText = true, VerticalAlignment = VAlignment.Center });
            // One Chop button for each way of cutting it, named for what it makes when there is a choice.
            for (var option = 0; item.Cuts > 0 && option < Math.Max(1, item.Options.Count); option++)
            {
                var text = item.Options.Count > 1
                    ? Loc.GetString("wg-prep-ui-chop-into", ("result", item.Options[option]))
                    : Loc.GetString("wg-prep-ui-chop");
                var tip = item.Options.Count > 0
                    ? Loc.GetString("wg-prep-ui-chop-into-tip", ("result", item.Options[option]))
                    : Loc.GetString("wg-prep-ui-chop-tip");
                var chop = new Button { Text = text, ToolTip = tip, ClipText = true, MaxWidth = 140 };
                var picked = item;
                var which = option;
                chop.OnPressed += _ => StartChop(picked, entMan, which);
                row.AddChild(chop);
            }

            var take = new Button { Text = Loc.GetString("wg-prep-ui-take") };
            var net = item.Entity;
            take.OnPressed += _ => OnTake?.Invoke(net);
            row.AddChild(take);
            _items.AddChild(row);
        }

        _recipes.RemoveAllChildren();
        var anyReady = false;
        if (state.Recipes.Count == 0)
            _recipes.AddChild(new Label { Text = Loc.GetString("wg-prep-ui-no-recipes"), StyleClasses = { "LabelSubText" } });

        foreach (var recipe in state.Recipes)
        {
            var ready = recipe.Missing.Count == 0;
            anyReady |= ready;
            var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            var title = recipe.Unbaked ? Loc.GetString("wg-prep-ui-unbaked", ("dish", recipe.Name)) : recipe.Name;
            box.AddChild(new Label { Text = title, FontColorOverride = ready ? ReadyColor : null });
            if (recipe.Have.Count > 0)
                box.AddChild(new Label { Text = Loc.GetString("wg-prep-ui-have", ("items", string.Join(", ", recipe.Have))), StyleClasses = { "LabelSubText" } });
            if (!ready)
                box.AddChild(new Label { Text = Loc.GetString("wg-prep-ui-missing", ("items", string.Join(", ", recipe.Missing))), FontColorOverride = MissingColor });
            else
                box.AddChild(new Label { Text = Loc.GetString(recipe.Unbaked ? "wg-prep-ui-ready-cook" : "wg-prep-ui-ready"), StyleClasses = { "LabelSubText" } });
            _recipes.AddChild(box);
        }

        _assemble.Disabled = !anyReady;
    }
}
