using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using EngineMapSystem = Robust.Server.GameObjects.MapSystem;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private void InitializeRiders()
    {
        // The engine deletes an emptied grid with everything on it; observers step off first.
        SubscribeLocalEvent<EmptyGridEvent>(OnGridEmpty, before: [typeof(EngineMapSystem)]);
    }

    private void OnGridEmpty(EmptyGridEvent ev)
    {
        if (Enabled)
            MoveRidersOff(ev.GridId);
    }

    /// <summary>
    /// Moves mapping observers, players, ghosts and bodies with a mind on a grid, at any depth, onto its map where they
    /// are, so deleting the grid doesn't take them along.
    /// </summary>
    public void MoveRidersOff(EntityUid grid)
    {
        if (!HasComp<MapGridComponent>(grid))
            return;

        _protection.MoveProtectedOff(grid);
    }
}
