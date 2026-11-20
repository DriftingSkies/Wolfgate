using Content.Shared._WG.Paintings;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Server._WG.Paintings;

/// <summary>Wall frames for finished canvases, gallery frames that hang catalogue paintings, and easels.</summary>
public sealed partial class WGPaintingFrameSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGCanvasSystem _canvas = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WGPaintingStoreSystem _store = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const string CutQuality = "Cutting";

    private static readonly SoundSpecifier CutSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg");

    /// <summary>Gallery frames that initialised before the catalogue loaded.</summary>
    private readonly List<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPaintingFrameComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WGPaintingFrameComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGPaintingFrameComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGPaintingFrameComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WGPaintingFrameComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGPaintingFrameComponent, EntRemovedFromContainerMessage>(OnRemoved);

        SubscribeLocalEvent<WGEaselComponent, InteractUsingEvent>(OnEaselInteractUsing);
        SubscribeLocalEvent<WGEaselComponent, InteractHandEvent>(OnEaselInteractHand);

        _store.IndexLoaded += OnIndexLoaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _store.IndexLoaded -= OnIndexLoaded;
    }

    private void OnInit(Entity<WGPaintingFrameComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, WGPaintingFrameComponent.ContainerId);
    }

    private void OnMapInit(Entity<WGPaintingFrameComponent> ent, ref MapInitEvent args)
    {
        if (Framed(ent) is { } canvas)
        {
            UpdateName(ent, canvas);
            return;
        }

        if (!ent.Comp.Gallery)
            return;

        if (_store.Loaded)
            Hang(ent);
        else
            _pending.Add(ent);
    }

    private void OnIndexLoaded()
    {
        foreach (var uid in _pending)
        {
            if (!TerminatingOrDeleted(uid) && TryComp<WGPaintingFrameComponent>(uid, out var frame))
                Hang((uid, frame));
        }

        _pending.Clear();
    }

    /// <summary>Hangs a print of a random catalogue painting that fits the frame.</summary>
    public async void Hang(Entity<WGPaintingFrameComponent> ent)
    {
        if (_store.PickRandom(ent.Comp.Large) is not { } entry)
            return;

        var pixels = await _store.GetPixelsAsync(entry.Hash);
        if (pixels == null || !_store.TryGetEntry(entry.Hash, out _) || TerminatingOrDeleted(ent) || Framed(ent) != null)
            return;

        if (_canvas.SpawnPrint(entry, pixels, Transform(ent).Coordinates) is not { } print)
            return;

        var slot = _container.EnsureContainer<ContainerSlot>(ent, WGPaintingFrameComponent.ContainerId);
        if (!_container.Insert(print, slot))
        {
            QueueDel(print);
            return;
        }

        UpdateName(ent, (print, Comp<WGCanvasComponent>(print)));
    }

    private Entity<WGCanvasComponent>? Framed(EntityUid frame)
    {
        if (!_container.TryGetContainer(frame, WGPaintingFrameComponent.ContainerId, out var container)
            || container.ContainedEntities.Count == 0)
            return null;

        var uid = container.ContainedEntities[0];
        return TryComp<WGCanvasComponent>(uid, out var canvas) ? (uid, canvas) : null;
    }

    private void OnInteractUsing(Entity<WGPaintingFrameComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var slot = _container.EnsureContainer<ContainerSlot>(ent, WGPaintingFrameComponent.ContainerId);
        if (TryComp<WGCanvasComponent>(args.Used, out var canvas))
        {
            args.Handled = true;
            if (slot.ContainedEntity != null)
                _popup.PopupEntity(Loc.GetString("wg-painting-frame-full"), ent, args.User);
            else if (!canvas.Finished)
                _popup.PopupEntity(Loc.GetString("wg-painting-frame-unfinished"), ent, args.User);
            else if (WGPaintingRules.GetSize(canvas.Width, canvas.Height)?.Large != ent.Comp.Large)
                _popup.PopupEntity(Loc.GetString("wg-painting-frame-wrong-size"), ent, args.User);
            else if (_hands.TryDrop(args.User, args.Used) && _container.Insert(args.Used, slot))
                UpdateName(ent, (args.Used, canvas));
            return;
        }

        if (slot.ContainedEntity is not { } framed || !_tool.HasQuality(args.Used, CutQuality))
            return;

        args.Handled = true;
        _container.Remove(framed, slot);
        _hands.PickupOrDrop(args.User, framed);
        _audio.PlayPvs(CutSound, ent);
        _popup.PopupEntity(Loc.GetString("wg-painting-frame-cut", ("user", args.User), ("frame", ent.Owner)), ent);
    }

    /// <summary>Opens a closer look at the framed painting.</summary>
    private void OnInteractHand(Entity<WGPaintingFrameComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || Framed(ent) is not { } canvas)
            return;

        args.Handled = true;
        if (!_ui.TryOpenUi(ent.Owner, WGPaintingViewUiKey.Key, args.User))
            return;

        var comp = canvas.Comp;
        _ui.SetUiState(ent.Owner, WGPaintingViewUiKey.Key,
            new WGCanvasUiState(comp.Width, comp.Height, comp.Pixels, true, comp.Title, comp.Artist));
    }

    private void OnRemoved(Entity<WGPaintingFrameComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != WGPaintingFrameComponent.ContainerId || TerminatingOrDeleted(ent))
            return;

        _ui.CloseUi(ent.Owner, WGPaintingViewUiKey.Key);

        if (MetaData(ent).EntityPrototype is { } proto)
            _meta.SetEntityName(ent, proto.Name);
    }

    private void UpdateName(Entity<WGPaintingFrameComponent> ent, Entity<WGCanvasComponent> canvas)
    {
        _meta.SetEntityName(ent, Loc.GetString("wg-painting-frame-name", ("title", canvas.Comp.Title)));
    }

    private void OnExamined(Entity<WGPaintingFrameComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (Framed(ent) is { } canvas)
        {
            args.PushMarkup(Loc.GetString("wg-canvas-examine-finished",
                ("title", FormattedMessage.EscapeText(canvas.Comp.Title)),
                ("artist", FormattedMessage.EscapeText(canvas.Comp.Artist))));
            args.PushMarkup(Loc.GetString("wg-painting-frame-examine-view"));
        }
        else
        {
            args.PushMarkup(Loc.GetString(ent.Comp.Large
                ? "wg-painting-frame-examine-large"
                : "wg-painting-frame-examine-small"));
        }

        if (ent.Comp.Gallery)
            args.PushMarkup(Loc.GetString("wg-painting-frame-examine-gallery"));
    }

    private EntityUid? EaselCanvas(EntityUid easel)
    {
        return _itemSlots.GetItemOrNull(easel, WGEaselComponent.SlotId);
    }

    private void OnEaselInteractUsing(Entity<WGEaselComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_canvas.IsPaintTool(args.Used) || EaselCanvas(ent) is not { } canvas)
            return;

        args.Handled = true;
        _canvas.TryOpen(canvas, args.User);
    }

    private void OnEaselInteractHand(Entity<WGEaselComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || EaselCanvas(ent) is not { } canvas)
            return;

        args.Handled = true;
        _canvas.TryOpen(canvas, args.User);
    }
}
