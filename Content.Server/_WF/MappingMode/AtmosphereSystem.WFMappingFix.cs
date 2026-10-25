using Content.Server.Atmos.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>Runs <c>fixgridatmos</c> on a grid with an atmosphere, for mappers who can't run the command.</summary>
    public void WfFixGridAtmos(EntityUid grid)
    {
        if (!HasComp<GridAtmosphereComponent>(grid))
            return;

        var id = GetNetEntity(grid).ToString();
        FixGridAtmosCommand(_consoleHost.LocalShell, id, new[] { id });
    }

    /// <summary>
    /// Points every pressure-damaged entity on a map at the grid it stands on. Copies and undos carry the source
    /// grid's reference, which saves as a dangling reference to another grid.
    /// </summary>
    public void WfRelinkDeltaPressure(EntityUid map)
    {
        var query = AllEntityQuery<DeltaPressureComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.MapUid != map || comp.GridUid == xform.GridUid)
                continue;

            if (comp.GridUid is { } old && Exists(old))
                TryRemoveDeltaPressureEntity(old, (uid, comp));

            comp.GridUid = null;
            comp.InProcessingList = false;
            if (xform.GridUid is not { } grid)
                continue;

            TryRemoveDeltaPressureEntity(grid, (uid, comp));
            TryAddDeltaPressureEntity(grid, (uid, comp));
        }
    }
}
