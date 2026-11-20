using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Station.Systems;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Administration;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.AdminObserver;

/// <summary>
/// The admin observer's kit. Hands a new observer its owner's saved layout, or the default kit when they have none,
/// saves the layout when they leave it, and gives and takes back single items for the Kit window.
/// </summary>
public sealed partial class WGAdminObserverKitSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;
    [Dependency] private WGAdminKitCatalogueSystem _catalogue = default!;
    [Dependency] private WGAdminObserverStore _store = default!;

    /// <summary>What an admin with no saved layout carries.</summary>
    public static readonly ProtoId<StartingGearPrototype> DefaultGear = "WGAdminObserverGear";

    /// <summary>The slot whose item takes what does not fit in a hand or pocket.</summary>
    private const string BagSlot = "back";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGAdminObserverComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<WGAdminObserverComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<WGAdminObserverComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeNetworkEvent<WGAdminKitSaveRequestEvent>(OnSaveRequest);
        _store.Loaded += OnStoreLoaded;
        _admin.OnPermsChanged += OnPermsChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _store.Loaded -= OnStoreLoaded;
        _admin.OnPermsChanged -= OnPermsChanged;
    }

    /// <summary>
    /// Keeps <see cref="WGAdminObserverComponent.SavesAnything"/> in step with the owner's Spawn flag, so an observer
    /// left after losing it saves only the catalogue.
    /// </summary>
    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        var spawn = _admin.HasAdminFlag(args.Player, AdminFlags.Spawn);
        var query = EntityQueryEnumerator<WGAdminObserverComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Owner == args.Player.UserId)
                comp.SavesAnything = spawn;
        }
    }

    private void OnPlayerAttached(Entity<WGAdminObserverComponent> ent, ref PlayerAttachedEvent args)
    {
        ent.Comp.SavedOnLeave = false;
        TryApply(ent, args.Player);
    }

    private void OnStoreLoaded(ICommonSession session)
    {
        if (session.AttachedEntity is { } uid && TryComp<WGAdminObserverComponent>(uid, out var comp))
            TryApply((uid, comp), session);
    }

    private void OnPlayerDetached(Entity<WGAdminObserverComponent> ent, ref PlayerDetachedEvent args)
    {
        ent.Comp.SavedOnLeave = Save(ent);
    }

    private void OnTerminating(Entity<WGAdminObserverComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!ent.Comp.SavedOnLeave)
            Save(ent);
    }

    private void OnSaveRequest(WGAdminKitSaveRequestEvent ev, EntitySessionEventArgs args)
    {
        if (TryGetObserver(args.SenderSession, out var observer) && Save(observer))
            _popup.PopupCursor(Loc.GetString("wg-observer-kit-saved"), args.SenderSession);
    }

    /// <summary>
    /// Hands an observer its kit once: the player's saved layout, the default kit when they have none, or the
    /// default kit unsaved for a player without the Admin flag. Waits when the saved document is still loading.
    /// </summary>
    private void TryApply(Entity<WGAdminObserverComponent> ent, ICommonSession player)
    {
        if (ent.Comp.KitApplied)
            return;

        if (!_store.IsAllowed(player))
        {
            ent.Comp.KitApplied = true;
            _spawning.EquipStartingGear(ent, DefaultGear);
            return;
        }

        if (_store.Get(player) is not { } data)
            return;

        ent.Comp.KitApplied = true;
        ent.Comp.Owner = player.UserId;
        ent.Comp.SavesKit = true;
        ent.Comp.SavesAnything = _admin.HasAdminFlag(player, AdminFlags.Spawn);

        if (data.Kit is not { } layout)
        {
            _spawning.EquipStartingGear(ent, DefaultGear);
            return;
        }

        var unplaced = Restore(ent, layout);
        if (unplaced.Count > 0)
            RaiseNetworkEvent(new WGAdminKitUnplacedEvent { Prototypes = unplaced, Replace = true }, player);
    }

    /// <summary>Opens the Kit window on a player's client.</summary>
    public void OpenWindow(ICommonSession player)
    {
        RaiseNetworkEvent(new WGAdminKitOpenEvent(), player);
    }

    /// <summary>Whether a player is an admin in an admin observer, and which.</summary>
    public bool TryGetObserver(ICommonSession player, out Entity<WGAdminObserverComponent> observer)
    {
        observer = default;
        if (!_store.IsAllowed(player)
            || player.AttachedEntity is not { } uid
            || !TryComp<WGAdminObserverComponent>(uid, out var comp))
        {
            return false;
        }

        comp.SavesAnything = _admin.HasAdminFlag(player, AdminFlags.Spawn);
        observer = (uid, comp);
        return true;
    }

    /// <summary>Writes what an observer carries to its owner's saved layout. False when it does not save.</summary>
    public bool Save(Entity<WGAdminObserverComponent> ent)
    {
        if (!ent.Comp.KitApplied || !ent.Comp.SavesKit || ent.Comp.Owner is not { } owner)
            return false;

        var layout = Snapshot(ent, ent.Comp.SavesAnything);
        return _store.ChangeLoaded(owner, data => data.Kit = layout);
    }

    /// <summary>
    /// Gives an observer a kit item: to the active hand, else a pocket, else the bag. False when it is not a kit
    /// item or there is no room; with no room the client is told, and nothing is left on the floor.
    /// </summary>
    public bool Give(Entity<WGAdminObserverComponent> ent, ICommonSession player, string id)
    {
        if (!_catalogue.Contains(id))
            return false;

        var item = Spawn(id, Transform(ent).Coordinates);
        if (!Place(ent, item))
        {
            Del(item);
            RaiseNetworkEvent(new WGAdminKitUnplacedEvent { Prototypes = new List<string> { id } }, player);
            return false;
        }

        Save(ent);
        return true;
    }

    /// <summary>Deletes the kit item in an observer's active hand. Returns its prototype, or null when it holds none.</summary>
    public string? PutAway(Entity<WGAdminObserverComponent> ent)
    {
        if (_hands.GetActiveItem(ent.Owner) is not { } held
            || MetaData(held).EntityPrototype?.ID is not { } id
            || !_catalogue.Contains(id))
        {
            return null;
        }

        Del(held);
        Save(ent);
        return id;
    }

    /// <summary>The prototype of what an observer holds in its active hand, kit item or not.</summary>
    public string? HeldPrototype(EntityUid uid)
    {
        return _hands.GetActiveItem(uid) is { } held ? MetaData(held).EntityPrototype?.ID : null;
    }

    private bool Place(EntityUid uid, EntityUid item)
    {
        if (TryComp<HandsComponent>(uid, out var hands)
            && hands.ActiveHand is { IsEmpty: true } active
            && _hands.TryPickup(uid, item, active, checkActionBlocker: false, animate: false, handsComp: hands))
        {
            return true;
        }

        if (_inventory.TryGetSlots(uid, out var slots))
        {
            foreach (var slot in slots)
            {
                if ((slot.SlotFlags & SlotFlags.POCKET) != 0 && _inventory.TryEquip(uid, item, slot.Name, silent: true))
                    return true;
            }
        }

        return _inventory.TryGetSlotEntity(uid, BagSlot, out var bag)
               && _storage.Insert(bag.Value, item, out _, playSound: false);
    }

    /// <summary>
    /// What an entity wears and holds, as a layout. Without <paramref name="saveAnything"/> only kit catalogue
    /// items are recorded, so something picked up in a round is not carried into the next.
    /// </summary>
    public WGKitLayout Snapshot(EntityUid uid, bool saveAnything)
    {
        var layout = new WGKitLayout();
        if (_inventory.TryGetSlots(uid, out var slots))
        {
            foreach (var slot in slots)
            {
                if (_inventory.TryGetSlotEntity(uid, slot.Name, out var item)
                    && Node(item.Value, saveAnything, 1) is { } node)
                {
                    layout.Slots[slot.Name] = node;
                }
            }
        }

        foreach (var hand in _hands.EnumerateHands(uid))
        {
            if (hand.HeldEntity is { } held && Node(held, saveAnything, 1) is { } node)
                layout.Hands[hand.Name] = node;
        }

        return layout;
    }

    private WGKitNode? Node(EntityUid item, bool saveAnything, int depth)
    {
        if (depth > WGAdminObserverData.MaxKitDepth
            || HasComp<VirtualItemComponent>(item)
            || MetaData(item).EntityPrototype?.ID is not { } id
            || !saveAnything && !_catalogue.Contains(id))
        {
            return null;
        }

        var node = new WGKitNode { Prototype = id };
        if (!TryComp<StorageComponent>(item, out var storage))
            return node;

        foreach (var (stored, location) in storage.StoredItems.OrderBy(s => s.Value.Position.Y).ThenBy(s => s.Value.Position.X))
        {
            if (Node(stored, saveAnything, depth + 1) is not { } child)
                continue;

            node.Contents.Add(new WGKitStored
            {
                Node = child,
                X = location.Position.X,
                Y = location.Position.Y,
                Rotation = (int) Math.Round(location.Rotation.Degrees),
            });
        }

        return node;
    }

    /// <summary>
    /// Dresses an entity from a layout. Returns the prototypes of the items that could not be placed; those are
    /// deleted, not dropped.
    /// </summary>
    public List<string> Restore(EntityUid uid, WGKitLayout layout)
    {
        var unplaced = new List<string>();
        var coords = Transform(uid).Coordinates;

        foreach (var (slot, node) in layout.Slots)
        {
            var item = Spawn(node.Prototype, coords);
            if (_inventory.TryEquip(uid, item, slot, silent: true, force: true))
            {
                RestoreContents(item, node, coords, unplaced);
                continue;
            }

            Del(item);
            Unplaced(node, unplaced);
        }

        foreach (var (hand, node) in layout.Hands)
        {
            var item = Spawn(node.Prototype, coords);
            if (_hands.TryPickup(uid, item, hand, checkActionBlocker: false, animate: false))
            {
                RestoreContents(item, node, coords, unplaced);
                continue;
            }

            Del(item);
            Unplaced(node, unplaced);
        }

        return unplaced;
    }

    /// <summary>
    /// Puts a node's saved contents into a freshly spawned container. An item the container already came with is
    /// moved to its saved place, not spawned twice; whatever else it came with stays.
    /// </summary>
    private void RestoreContents(EntityUid container, WGKitNode node, EntityCoordinates coords, List<string> unplaced)
    {
        if (node.Contents.Count == 0)
            return;

        if (!TryComp<StorageComponent>(container, out var storage))
        {
            foreach (var stored in node.Contents)
            {
                Unplaced(stored.Node, unplaced);
            }

            return;
        }

        var fill = storage.Container.ContainedEntities.ToList();
        foreach (var stored in node.Contents)
        {
            var location = new ItemStorageLocation(Angle.FromDegrees(stored.Rotation), new Vector2i(stored.X, stored.Y));
            var index = fill.FindIndex(e => MetaData(e).EntityPrototype?.ID == stored.Node.Prototype);
            if (index >= 0)
            {
                var existing = fill[index];
                fill.RemoveAt(index);
                _storage.TrySetItemStorageLocation(existing, (container, storage), location);
                RestoreContents(existing, stored.Node, coords, unplaced);
                continue;
            }

            var item = Spawn(stored.Node.Prototype, coords);
            if (_storage.InsertAt((container, storage), item, location, out _, playSound: false, stackAutomatically: false)
                || _storage.Insert(container, item, out _, storageComp: storage, playSound: false, stackAutomatically: false))
            {
                RestoreContents(item, stored.Node, coords, unplaced);
                continue;
            }

            Del(item);
            Unplaced(stored.Node, unplaced);
        }
    }

    private static void Unplaced(WGKitNode node, List<string> unplaced)
    {
        unplaced.Add(node.Prototype);
        foreach (var stored in node.Contents)
        {
            Unplaced(stored.Node, unplaced);
        }
    }
}
