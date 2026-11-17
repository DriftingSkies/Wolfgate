using Content.Server._WG.ShipTiers;
using Content.Shared._WG.Operations;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Operations;

/// <summary>Lists an operation only to those who can call it now (see <see cref="WGOperationSystem.CanCall"/>).</summary>
public sealed partial class WGOperationReadyCondition : ListingCondition
{
    [DataField(required: true)]
    public ProtoId<WGOperationPrototype> Operation;

    public override bool Condition(ListingConditionArgs args)
    {
        return args.EntityManager.System<WGOperationSystem>().CanCall(WGListingBuyer.Body(args), Operation);
    }
}
