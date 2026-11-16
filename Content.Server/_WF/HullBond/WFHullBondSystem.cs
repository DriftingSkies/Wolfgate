using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Server.Physics;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Server._WF.HullBond;

/// <summary>
/// Bonds ship fittings (<see cref="WFHullBondComponent"/>) to the hull they are first bolted to. A bonded fitting can
/// be unbolted and moved around its own ship but refuses to bolt down on any other. Its board takes the bond with it
/// when the fitting is taken apart, so rebuilding it elsewhere doesn't get around the bond.
/// </summary>
public sealed partial class WFHullBondSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFHullBondComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFHullBondComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<WFHullBondComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<WFHullBondComponent, UserAnchoredEvent>(OnUserAnchored);
        SubscribeLocalEvent<WFHullBondComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<WFHullBondComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<WFHullBondComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<WFHullBondComponent, ItemSlotInsertAttemptEvent>(OnSlotInsertAttempt);
        SubscribeLocalEvent<WFHullBondComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    /// <summary>True when the fitting is unbonded or bonded to the hull it stands on.</summary>
    public bool FitsHere(Entity<WFHullBondComponent> ent)
    {
        return ent.Comp.Hull == null || Transform(ent).GridUid is { } grid && HullId(grid) == ent.Comp.Hull;
    }

    private void OnMapInit(Entity<WFHullBondComponent> ent, ref MapInitEvent args)
    {
        if (!ent.Comp.Pending)
        {
            BondIfBolted(ent);
            return;
        }

        // Built from a bonded board: construction bolts the new machine down without asking.
        ent.Comp.Pending = false;
        var xform = Transform(ent);
        if (!xform.Anchored || FitsHere(ent))
            return;

        _transform.Unanchor(ent, xform);
        _popup.PopupEntity(RefusalText(ent), ent, PopupType.MediumCaution);
    }

    private void OnAnchorChanged(Entity<WFHullBondComponent> ent, ref AnchorStateChangedEvent args)
    {
        // Bolted down by anything but a wrench, such as a repair rebuilding it. Map init handles a fitting still loading.
        if (args.Anchored && LifeStage(ent) >= EntityLifeStage.MapInitialized)
            BondIfBolted(ent);
    }

    private void OnAnchorAttempt(Entity<WFHullBondComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled || FitsHere(ent))
            return;

        _popup.PopupEntity(RefusalText(ent), ent, args.User);
        args.Cancel();
    }

    private void OnUserAnchored(Entity<WFHullBondComponent> ent, ref UserAnchoredEvent args)
    {
        // The attempt is only checked when the wrench starts, and the fitting may have been dragged since.
        if (FitsHere(ent))
        {
            BondIfBolted(ent);
            return;
        }

        _transform.Unanchor(ent, Transform(ent));
        _popup.PopupEntity(RefusalText(ent), ent, args.User);
    }

    private void OnUnanchorAttempt(Entity<WFHullBondComponent> ent, ref UnanchorAttemptEvent args)
    {
        // A hull loaded from a save never map-inits, so its fittings bond here at the latest.
        BondIfBolted(ent);
    }

    private void OnInserted(Entity<WFHullBondComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (ent.Comp.Hull != null || !TryComp<WFHullBondComponent>(args.Entity, out var board) || board.Hull == null)
            return;

        ent.Comp.Hull = board.Hull;
        ent.Comp.HullName = board.HullName;
        ent.Comp.Pending = LifeStage(ent) < EntityLifeStage.MapInitialized;
    }

    private void OnRemoved(Entity<WFHullBondComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        // A hull being deleted empties every container aboard.
        if (TerminatingOrDeleted(args.Entity) || TerminatingOrDeleted(ent))
            return;

        if (!HasComp<MachineBoardComponent>(args.Entity) && !HasComp<ComputerBoardComponent>(args.Entity))
            return;

        BondIfBolted(ent);
        if (ent.Comp.Hull == null)
            return;

        var board = EnsureComp<WFHullBondComponent>(args.Entity);
        board.Hull = ent.Comp.Hull;
        board.HullName = ent.Comp.HullName;
    }

    private void OnSlotInsertAttempt(Entity<WFHullBondComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        // A flatpack would unpack as a new, unbonded machine.
        if (args.Item == ent.Owner && ent.Comp.Hull != null && HasComp<FlatpackCreatorComponent>(args.SlotEntity))
            args.Cancelled = true;
    }

    private void OnExamined(Entity<WFHullBondComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        BondIfBolted(ent);
        var fitting = HasComp<AnchorableComponent>(ent);
        if (ent.Comp.Hull == null)
        {
            if (fitting)
                args.PushMarkup(Loc.GetString("wf-hull-bond-examine-unbonded"));
            return;
        }

        var here = Transform(ent).GridUid is { } grid && HullId(grid) == ent.Comp.Hull ? grid : (EntityUid?) null;
        var ship = FormattedMessage.EscapeText(here is { } hull ? Name(hull) : HullName(ent));
        var key = !fitting ? "wf-hull-bond-examine-board" : here != null ? "wf-hull-bond-examine-here" : "wf-hull-bond-examine-away";
        args.PushMarkup(Loc.GetString(key, ("ship", ship)));
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        // The pieces of a broken hull are still that hull.
        if (!TryComp<WFHullBondIdComponent>(args.Grid, out var source))
            return;

        foreach (var grid in args.NewGrids)
        {
            var piece = EnsureComp<WFHullBondIdComponent>(grid);
            piece.Id = source.Id;
            piece.Loose = source.Loose;
        }
    }

    /// <summary>
    /// Makes a hull loose: the fittings already bonded to it lose their bond, and nothing bonds to it again. A stripped
    /// fitting then bonds to the first other hull it is bolted to.
    /// </summary>
    public void Loosen(EntityUid grid)
    {
        var id = EnsureComp<WFHullBondIdComponent>(grid);
        id.Loose = true;
        if (id.Id.Length == 0)
            return;

        var query = EntityQueryEnumerator<WFHullBondComponent>();
        while (query.MoveNext(out _, out var bond))
        {
            if (bond.Hull != id.Id)
                continue;

            bond.Hull = null;
            bond.HullName = null;
        }
    }

    /// <summary>Bonds a bolted-down fitting that has no bond yet to the hull it stands on.</summary>
    private void BondIfBolted(Entity<WFHullBondComponent> ent)
    {
        if (ent.Comp.Hull != null || ent.Comp.Pending)
            return;

        var xform = Transform(ent);
        if (!xform.Anchored || xform.GridUid is not { } grid || TerminatingOrDeleted(grid))
            return;

        var id = EnsureComp<WFHullBondIdComponent>(grid);
        if (id.Loose)
            return;

        if (id.Id.Length == 0)
            id.Id = Guid.NewGuid().ToString("N");

        ent.Comp.Hull = id.Id;
        ent.Comp.HullName = Name(grid);
    }

    private string? HullId(EntityUid grid)
    {
        return TryComp<WFHullBondIdComponent>(grid, out var id) && id.Id.Length > 0 ? id.Id : null;
    }

    private string HullName(Entity<WFHullBondComponent> ent)
    {
        return string.IsNullOrWhiteSpace(ent.Comp.HullName) ? Loc.GetString("wf-hull-bond-unknown-ship") : ent.Comp.HullName;
    }

    private string RefusalText(Entity<WFHullBondComponent> ent)
    {
        return Loc.GetString("wf-hull-bond-anchor-fail", ("ship", HullName(ent)));
    }
}
