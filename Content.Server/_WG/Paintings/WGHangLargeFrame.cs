using System.Numerics;
using Content.Shared.Construction;
using Content.Shared.Coordinates.Helpers;
using JetBrains.Annotations;

namespace Content.Server._WG.Paintings;

/// <summary>
/// Construction action: hangs a large frame across its wall tile and the one east of it, so the painting stays on
/// wall tiles the viewer sees instead of overhanging tiles in shadow.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class WGHangLargeFrame : IGraphAction
{
    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        var xform = entityManager.GetComponent<TransformComponent>(uid);
        if (xform.Anchored)
            return;

        var tile = xform.Coordinates.SnapToGrid(entityManager);
        entityManager.System<SharedTransformSystem>().SetCoordinates(uid, tile.Offset(new Vector2(0.5f, 0f)));
    }
}
