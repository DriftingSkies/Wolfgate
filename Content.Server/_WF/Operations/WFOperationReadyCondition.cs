using Content.Shared._WF.Operations;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Operations;

/// <summary>Lists an operation only to those who can call it now (see <see cref="WFOperationSystem.CanCall"/>).</summary>
public sealed partial class WFOperationReadyCondition : ListingCondition
{
    [DataField(required: true)]
    public ProtoId<WFOperationPrototype> Operation;

    public override bool Condition(ListingConditionArgs args)
    {
        return args.EntityManager.System<WFOperationSystem>().CanCall(args.Buyer, Operation);
    }
}
