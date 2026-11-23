using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Robust.Shared.Map;

namespace Content.Shared._WG.Construction;

/// <summary>Construction condition: no APC or APC frame on the tile already faces the build direction.</summary>
[DataDefinition]
public sealed partial class WGApcFaceFree : IConstructionCondition
{
    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "wg-construction-condition-apc-face-free",
        };
    }

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        return !IoCManager.Resolve<IEntityManager>().System<WGApcFaceSystem>().IsTaken(location, direction);
    }
}
