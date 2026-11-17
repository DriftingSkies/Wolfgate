using Content.Server._WG.ShipTiers;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.SpecialistKits;

/// <summary>Shows a kit's replacement listing only to a player who drew that kit this round.</summary>
public sealed partial class WGKitHolderCondition : ListingCondition
{
    [DataField(required: true)]
    public EntProtoId Kit;

    public override bool Condition(ListingConditionArgs args)
    {
        return args.EntityManager.System<WGSpecialistKitSystem>().Holds(WGListingBuyer.Body(args), Kit);
    }
}
