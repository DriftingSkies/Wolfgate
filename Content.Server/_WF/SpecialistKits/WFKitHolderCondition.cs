using Content.Server._WF.ShipTiers;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SpecialistKits;

/// <summary>Shows a kit's replacement listing only to a player who drew that kit this round.</summary>
public sealed partial class WFKitHolderCondition : ListingCondition
{
    [DataField(required: true)]
    public EntProtoId Kit;

    public override bool Condition(ListingConditionArgs args)
    {
        return args.EntityManager.System<WFSpecialistKitSystem>().Holds(WFListingBuyer.Body(args), Kit);
    }
}
