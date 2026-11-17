using Content.Server._WG.Remade.Gifts;
using Content.Server.Administration.Logs;
using Content.Shared._WG.Remade;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Monolith;

/// <summary>The Tap console: sets a Monolith's draw and feeds it corpses to bring its resonance down.</summary>
public sealed partial class WGMonolithTapSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private WGMatterSystem _matter = default!;
    [Dependency] private WGMonolithSystem _monolith = default!;

    private static readonly TimeSpan RefreshRate = TimeSpan.FromSeconds(1);

    private readonly HashSet<Entity<WGMonolithComponent>> _inReach = new();
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMonolithTapComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WGMonolithTapComponent, WGMonolithTapSetDrawMessage>(OnSetDraw);
        SubscribeLocalEvent<WGMonolithTapComponent, DragDropTargetEvent>(OnDragDrop);
        SubscribeLocalEvent<WGMonolithTapComponent, WGMonolithFeedDoAfterEvent>(OnFeed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshRate;
        var query = EntityQueryEnumerator<WGMonolithTapComponent>();
        while (query.MoveNext(out var uid, out var tap))
        {
            if (_ui.IsUiOpen(uid, WGMonolithTapUiKey.Key))
                SendState((uid, tap));
        }
    }

    private void OnOpened(Entity<WGMonolithTapComponent> ent, ref BoundUIOpenedEvent args)
    {
        SendState(ent);
    }

    private void OnSetDraw(Entity<WGMonolithTapComponent> ent, ref WGMonolithTapSetDrawMessage args)
    {
        if (FindMonolith(ent) is not { } monolith)
            return;

        _monolith.SetDial(monolith, args.Gift, args.Draw);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):user} set the {args.Gift} dial of {ToPrettyString(monolith):monolith} to {_monolith.GetDial(monolith, args.Gift):P0}");
        SendState(ent);
    }

    private void OnDragDrop(Entity<WGMonolithTapComponent> ent, ref DragDropTargetEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(args.Dragged) || !_mobState.IsDead(args.Dragged))
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.FeedTime, new WGMonolithFeedDoAfterEvent(), ent, args.Dragged, ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
        });
    }

    private void OnFeed(Entity<WGMonolithTapComponent> ent, ref WGMonolithFeedDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } corpse || !_mobState.IsDead(corpse))
            return;

        args.Handled = true;
        if (FindMonolith(ent) is not { } monolith)
        {
            _popup.PopupEntity(Loc.GetString("wg-monolith-tap-no-scion"), ent, args.User);
            return;
        }

        if (!_monolith.TryQuiet(monolith))
        {
            _popup.PopupEntity(Loc.GetString("wg-monolith-tap-refused"), ent, args.User);
            return;
        }

        foreach (var item in _inventory.GetHandOrInventoryEntities(corpse))
        {
            _transform.DropNextTo(item, ent.Owner);
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(args.User):user} fed {ToPrettyString(corpse):corpse} to {ToPrettyString(monolith):monolith}");
        _popup.PopupEntity(Loc.GetString("wg-monolith-tap-fed", ("corpse", corpse)), ent, PopupType.LargeCaution);
        QueueDel(corpse);
        SendState(ent);
    }

    /// <summary>The nearest Monolith in the Tap's reach.</summary>
    private Entity<WGMonolithComponent>? FindMonolith(Entity<WGMonolithTapComponent> ent)
    {
        _inReach.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _inReach);

        Entity<WGMonolithComponent>? nearest = null;
        var best = float.MaxValue;
        var origin = _transform.GetWorldPosition(ent);
        foreach (var monolith in _inReach)
        {
            var distance = (_transform.GetWorldPosition(monolith) - origin).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = monolith;
            }
        }

        return nearest;
    }

    private void SendState(Entity<WGMonolithTapComponent> ent)
    {
        WGMonolithTapState state;
        if (FindMonolith(ent) is { } monolith)
        {
            var dials = new float[Enum.GetValues<WGGift>().Length];
            foreach (var gift in Enum.GetValues<WGGift>())
            {
                dials[(int) gift] = _monolith.GetDial(monolith, gift);
            }

            var rate = WGMonolithMath.Rate(_monolith.EffectiveDraw(monolith), monolith.Comp.Resonance, monolith.Comp.Corpses);
            state = new WGMonolithTapState(
                monolith.Comp.Resonance,
                monolith.Comp.Stage,
                dials,
                WGMonolithMath.Output(dials[(int) WGGift.Power], monolith.Comp.Resonance),
                rate * 60f,
                _monolith.IsBarred(monolith),
                _matter.FindSilo(monolith) != null,
                monolith.Comp.Shards.Count);
        }
        else
        {
            state = new WGMonolithTapState(null, WGMonolithStage.Quiet, new float[Enum.GetValues<WGGift>().Length], 0f, 0f, false, false, 0);
        }

        _ui.SetUiState(ent.Owner, WGMonolithTapUiKey.Key, state);
    }
}
