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
}
