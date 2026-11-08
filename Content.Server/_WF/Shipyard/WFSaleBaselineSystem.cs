using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Encounters.Systems;

namespace Content.Server._WF.Shipyard;

/// <summary>Takes a free hull's handed-out value off its appraisal, so selling it pays only for what was added.</summary>
public sealed class WFSaleBaselineSystem : EntitySystem
{
    [Dependency] private ShipyardSystem _shipyard = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSaleBaselineComponent, WFGridAppraisedEvent>(OnAppraised);
    }

    /// <summary>Records the hull's sale appraisal now as the value it can never be sold for.</summary>
    public void SetBaseline(EntityUid hull)
    {
        RemComp<WFSaleBaselineComponent>(hull);
        var baseline = _shipyard.GetShipAppraisal(hull);
        EnsureComp<WFSaleBaselineComponent>(hull).Baseline = baseline;
    }

    private void OnAppraised(Entity<WFSaleBaselineComponent> ent, ref WFGridAppraisedEvent args)
    {
        args.Price = Math.Max(0, args.Price - ent.Comp.Baseline);
    }
}
