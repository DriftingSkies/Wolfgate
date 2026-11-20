using Content.Server.Administration.Logs;
using Content.Server.Crayon;
using Content.Shared._WG.Paintings;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.Paintings;

/// <summary>Painting canvases with the held crayon or palette, finishing them, and printing catalogue copies.</summary>
public sealed partial class WGCanvasSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WGPaintingStoreSystem _store = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCanvasComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WGCanvasComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGCanvasComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGCanvasComponent, ExaminedEvent>(OnExamined);

        Subs.BuiEvents<WGCanvasComponent>(WGCanvasUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>((uid, comp, _) => UpdateUi((uid, comp)));
            subs.Event<WGCanvasPaintMessage>(OnPaint);
            subs.Event<WGCanvasPickColorMessage>(OnPickColor);
            subs.Event<WGCanvasSwatchMessage>(OnSwatch);
            subs.Event<WGCanvasFinishMessage>(OnFinish);
        });
    }

    private void OnStartup(Entity<WGCanvasComponent> ent, ref ComponentStartup args)
    {
        var comp = ent.Comp;
        var length = comp.Width * comp.Height * 4;
        comp.Pixels = Decode(comp.Data, length) ?? WGPaintingRules.BlankPixels(comp.Width, comp.Height);
        if (!comp.Finished)
            return;

        comp.Art = comp.Pixels;
        if (string.IsNullOrEmpty(comp.Hash))
            comp.Hash = WGPaintingStoreSystem.Hash(comp.Width, comp.Height, comp.Pixels);
    }

    private void OnMapInit(Entity<WGCanvasComponent> ent, ref MapInitEvent args)
    {
        // A saved copy of a painting blocked since.
        if (ent.Comp.Finished && _store.IsBlocked(ent.Comp.Hash))
            QueueDel(ent);
    }

    private static byte[]? Decode(string data, int length)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        var bytes = new byte[length];
        return Convert.TryFromBase64String(data, bytes, out var written) && written == length ? bytes : null;
    }

    private void OnInteractUsing(Entity<WGCanvasComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !IsPaintTool(args.Used))
            return;

        args.Handled = true;
        _ui.TryOpenUi(ent.Owner, WGCanvasUiKey.Key, args.User);
    }

    private void OnExamined(Entity<WGCanvasComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        if (!comp.Finished)
        {
            args.PushMarkup(Loc.GetString(comp.Used ? "wg-canvas-examine-wip" : "wg-canvas-examine-blank"));
            return;
        }

        args.PushMarkup(Loc.GetString("wg-canvas-examine-finished",
            ("title", FormattedMessage.EscapeText(comp.Title)), ("artist", FormattedMessage.EscapeText(comp.Artist))));
        if (comp.Print)
            args.PushMarkup(Loc.GetString("wg-canvas-examine-print"));
    }

    /// <summary>Opens a canvas for a user, as an easel does with the canvas it holds.</summary>
    public void TryOpen(EntityUid canvas, EntityUid user)
    {
        _ui.TryOpenUi(canvas, WGCanvasUiKey.Key, user);
    }

    public bool IsPaintTool(EntityUid uid)
    {
        return HasComp<CrayonComponent>(uid) || HasComp<WGPaintPaletteComponent>(uid);
    }

    /// <summary>The colour of the paint tool a user holds, the active hand first.</summary>
    public bool TryGetPaintColor(EntityUid user, out Color color, out EntityUid tool)
    {
        if (_hands.GetActiveItem(user) is { } active && TryColor(active, out color))
        {
            tool = active;
            return true;
        }

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryColor(held, out color))
            {
                tool = held;
                return true;
            }
        }

        color = default;
        tool = default;
        return false;
    }

    private bool TryColor(EntityUid uid, out Color color)
    {
        if (TryComp<WGPaintPaletteComponent>(uid, out var palette))
        {
            color = palette.Selected;
            return true;
        }

        if (TryComp<CrayonComponent>(uid, out var crayon))
        {
            color = crayon.Color;
            return true;
        }

        color = default;
        return false;
    }

    private void OnPaint(Entity<WGCanvasComponent> ent, ref WGCanvasPaintMessage args)
    {
        var comp = ent.Comp;
        if (comp.Finished || args.Pixels.Length > WGPaintingRules.MaxStrokePixels)
            return;

        if (!TryGetPaintColor(args.Actor, out var color, out _))
        {
            _popup.PopupEntity(Loc.GetString("wg-canvas-no-tool"), ent, args.Actor);
            return;
        }

        var paint = args.Erase ? WGPaintingRules.Blank : color;
        var count = comp.Width * comp.Height;
        foreach (var index in args.Pixels)
        {
            if (index < count)
                WGPaintingRules.Write(comp.Pixels, index * 4, paint);
        }

        comp.Data = Convert.ToBase64String(comp.Pixels);
        if (!comp.Used && !args.Erase)
        {
            comp.Used = true;
            Dirty(ent);
        }

        UpdateUi(ent);
    }

    private void OnPickColor(Entity<WGCanvasComponent> ent, ref WGCanvasPickColorMessage args)
    {
        if (!TryGetPaintColor(args.Actor, out _, out var tool)
            || !TryComp<WGPaintPaletteComponent>(tool, out var palette))
            return;

        var color = WGPaintingRules.Quantize(args.Color);
        if (palette.Selected == color)
            return;

        palette.Recent.Remove(palette.Selected);
        palette.Recent.Insert(0, palette.Selected);
        palette.Recent.Remove(color);
        if (palette.Recent.Count > WGPaintingRules.MaxRecent)
            palette.Recent.RemoveRange(WGPaintingRules.MaxRecent, palette.Recent.Count - WGPaintingRules.MaxRecent);
        palette.Selected = color;
        Dirty(tool, palette);
    }

    private void OnSwatch(Entity<WGCanvasComponent> ent, ref WGCanvasSwatchMessage args)
    {
        if (!TryGetPaintColor(args.Actor, out _, out var tool)
            || !TryComp<WGPaintPaletteComponent>(tool, out var palette))
            return;

        var color = WGPaintingRules.Quantize(args.Color);
        if (args.Remove)
        {
            if (!palette.Swatches.Remove(color))
                return;
        }
        else if (palette.Swatches.Contains(color) || palette.Swatches.Count >= WGPaintingRules.MaxSwatches)
        {
            return;
        }
        else
        {
            palette.Swatches.Add(color);
        }

        Dirty(tool, palette);
    }

    private void OnFinish(Entity<WGCanvasComponent> ent, ref WGCanvasFinishMessage args)
    {
        var comp = ent.Comp;
        if (comp.Finished)
            return;

        var title = args.Title.Trim();
        if (title.Length > WGPaintingRules.MaxTitleLength)
            title = title[..WGPaintingRules.MaxTitleLength];
        if (title.Length == 0)
            title = Loc.GetString("wg-canvas-untitled");

        var artist = args.Anonymous ? Loc.GetString("wg-canvas-anonymous") : Name(args.Actor);
        Guid? author = TryComp<ActorComponent>(args.Actor, out var actor) ? actor.PlayerSession.UserId.UserId : null;
        Finish(ent, title, artist, author);
        _adminLog.Add(LogType.WGPainting, LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} finished painting \"{title}\" signed {artist} ({comp.Hash})");
    }

    /// <summary>Locks the canvas as a finished painting.</summary>
    private void Finish(Entity<WGCanvasComponent> ent, string title, string artist, Guid? author)
    {
        var comp = ent.Comp;
        comp.Finished = true;
        comp.Title = title;
        comp.Artist = artist;
        comp.AuthorUserId = author;
        comp.Hash = WGPaintingStoreSystem.Hash(comp.Width, comp.Height, comp.Pixels);
        comp.Data = Convert.ToBase64String(comp.Pixels);
        comp.Art = comp.Pixels;
        _meta.SetEntityName(ent, Loc.GetString("wg-canvas-finished-name", ("title", title)));
        Dirty(ent);
        UpdateUi(ent);
    }

    /// <summary>Spawns a print of a catalogue painting, or null when it has no canvas of its size.</summary>
    public EntityUid? SpawnPrint(WGPaintingEntry entry, byte[] pixels, EntityCoordinates coordinates)
    {
        if (WGPaintingRules.GetSize(entry.Width, entry.Height) is not { } size
            || pixels.Length != entry.Width * entry.Height * 4)
            return null;

        var uid = Spawn(size.Prototype, coordinates);
        var comp = Comp<WGCanvasComponent>(uid);
        comp.Pixels = (byte[]) pixels.Clone();
        comp.Used = true;
        comp.Print = true;
        Finish((uid, comp), entry.Title, entry.Artist, entry.Author);
        return uid;
    }

    private void UpdateUi(Entity<WGCanvasComponent> ent)
    {
        var comp = ent.Comp;
        _ui.SetUiState(ent.Owner, WGCanvasUiKey.Key,
            new WGCanvasUiState(comp.Width, comp.Height, comp.Pixels, comp.Finished, comp.Title, comp.Artist));
    }
}
