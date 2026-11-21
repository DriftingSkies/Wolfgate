using System.Linq;
using Content.Server._WG.Farming;
using Content.Server.Popups;
using Content.Shared._WG.Farming;
using Content.Shared._WG.Fishing;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Fishing;

/// <summary>A crab cage: baited and set in water, it fills with a shellfish now and then without anyone watching it.</summary>
[RegisterComponent, Access(typeof(WGCrabCageSystem))]
public sealed partial class WGCrabCageComponent : Component
{
    /// <summary>Minutes a baited cage takes to catch something.</summary>
    [DataField]
    public float Minutes = 8f;

    /// <summary>Bait loaded, each its own quality; one goes with every catch.</summary>
    [DataField]
    public List<int> Bait = new();

    [DataField]
    public int MaxBait = 5;

    [ViewVariables]
    public bool Set;

    [ViewVariables]
    public string? Table;

    [ViewVariables]
    public TimeSpan ReadyAt;

    /// <summary>Where it was set; moved from here, it stops fishing.</summary>
    [ViewVariables]
    public MapCoordinates SetAt;

    /// <summary>What it holds, waiting to be emptied, and its quality.</summary>
    [ViewVariables]
    public EntProtoId? Catch;

    [ViewVariables]
    public WGCropQuality CatchQuality;
}

/// <summary>Sets, fills and empties crab cages.</summary>
public sealed class WGCrabCageSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGFishingSystem _fishing = default!;

    private static readonly ProtoId<WGFishLootPrototype> LootId = "WGFishLoot";
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCrabCageComponent, AfterInteractEvent>(OnSet);
        SubscribeLocalEvent<WGCrabCageComponent, InteractUsingEvent>(OnBait);
        SubscribeLocalEvent<WGCrabCageComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<WGCrabCageComponent, ExaminedEvent>(OnExamined);
    }

    private void OnSet(Entity<WGCrabCageComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach && !_transform.InRange(Transform(args.User).Coordinates, args.ClickLocation, 1.5f))
            return;

        string? table;
        if (args.Target is { } target && HasComp<WGFishingSpotComponent>(target))
            table = _fishing.PickTable(args.User, pond: true);
        else if (_fishing.IsOpenWater(args.ClickLocation))
            table = _fishing.PickTable(args.User, pond: false);
        else
            return;

        args.Handled = true;
        if (table == null)
        {
            _popup.PopupEntity(Loc.GetString("wg-fish-nothing-here"), args.User, args.User);
            return;
        }

        if (ent.Comp.Bait.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("wg-cage-needs-bait"), ent, args.User);
            return;
        }

        // Let go of it first, so it leaves the hand rather than being moved inside it.
        if (!_hands.TryDrop(args.User, ent, args.ClickLocation))
            return;
        _transform.SetCoordinates(ent, args.ClickLocation);
        ent.Comp.SetAt = _transform.GetMapCoordinates(ent);
        ent.Comp.Set = true;
        ent.Comp.Table = table;
        ent.Comp.Catch = null;
        ent.Comp.ReadyAt = _timing.CurTime + TimeSpan.FromMinutes(ent.Comp.Minutes);
        _popup.PopupEntity(Loc.GetString("wg-cage-set"), ent, args.User);
    }

    private void OnBait(Entity<WGCrabCageComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<WGBaitComponent>(args.Used, out var bait))
            return;

        args.Handled = true;
        if (ent.Comp.Bait.Count >= ent.Comp.MaxBait)
        {
            _popup.PopupEntity(Loc.GetString("wg-fish-bait-full"), ent, args.User);
            return;
        }

        ent.Comp.Bait.Add(bait.Quality);
        if (TryComp<StackComponent>(args.Used, out var stack) && stack.Count > 1)
            _stack.SetCount(args.Used, stack.Count - 1, stack);
        else
            QueueDel(args.Used);
        if (ent.Comp.Set && ent.Comp.Catch == null && ent.Comp.Bait.Count == 1)
            ent.Comp.ReadyAt = _timing.CurTime + TimeSpan.FromMinutes(ent.Comp.Minutes);
        _popup.PopupEntity(Loc.GetString("wg-fish-baited", ("count", ent.Comp.Bait.Count)), ent, args.User);
    }

    /// <summary>Empties a cage that has caught something; the next catch starts if bait is left.</summary>
    private void OnActivate(Entity<WGCrabCageComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !ent.Comp.Set || ent.Comp.Catch is not { } caught)
            return;

        args.Handled = true;
        var item = Spawn(caught, Transform(args.User).Coordinates);
        if (ent.Comp.CatchQuality != WGCropQuality.Normal)
            EnsureComp<WGProduceQualityComponent>(item).Quality = ent.Comp.CatchQuality;
        _hands.PickupOrDrop(args.User, item);
        _popup.PopupEntity(Loc.GetString("wg-cage-emptied", ("item", item)), ent, args.User, PopupType.Medium);
        ent.Comp.Catch = null;
        ent.Comp.ReadyAt = _timing.CurTime + TimeSpan.FromMinutes(ent.Comp.Minutes);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _next)
            return;
        _next = now + Tick;

        var query = EntityQueryEnumerator<WGCrabCageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var cage, out var xform))
        {
            if (!cage.Set)
                continue;

            // Picked up or moved off its spot: it comes out with whatever it held.
            var here = _transform.GetMapCoordinates(uid, xform);
            if (xform.ParentUid != xform.GridUid && xform.ParentUid != xform.MapUid
                || here.MapId != cage.SetAt.MapId || (here.Position - cage.SetAt.Position).Length() > 0.5f)
            {
                cage.Set = false;
                if (cage.Catch is { } held)
                {
                    var item = Spawn(held, here);
                    if (cage.CatchQuality != WGCropQuality.Normal)
                        EnsureComp<WGProduceQualityComponent>(item).Quality = cage.CatchQuality;
                    cage.Catch = null;
                }
                continue;
            }

            if (cage.Catch != null || cage.Bait.Count == 0 || now < cage.ReadyAt)
                continue;

            if (cage.Table is not { } tableId || !_proto.TryIndex<WGFishTablePrototype>(tableId, out var table))
                continue;

            if (!_proto.TryIndex(LootId, out var loot) || loot.CageFallback.Count == 0)
                continue;
            var bait = cage.Bait[^1];
            cage.Bait.RemoveAt(cage.Bait.Count - 1);
            // The water's own shellfish if it has any, else any shellfish.
            var pool = table.Fish.Where(f => loot.CageFallback.Any(c => c.Id == f.Id)).ToList();
            if (pool.Count == 0)
                pool = loot.CageFallback;

            cage.Catch = _fishing.Pick(pool);
            cage.CatchQuality = _fishing.Roll(bait);
        }
    }

    private void OnExamined(Entity<WGCrabCageComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-cage-bait", ("bait", ent.Comp.Bait.Count)));
        if (!ent.Comp.Set)
            return;
        args.PushMarkup(Loc.GetString(ent.Comp.Catch != null ? "wg-cage-full"
            : ent.Comp.Bait.Count == 0 ? "wg-cage-no-bait" : "wg-cage-waiting"));
    }
}
