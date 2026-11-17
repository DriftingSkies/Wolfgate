using Content.Server.Pinpointer;
using Content.Server.Shuttles.Components;

namespace Content.Server._WG.Shuttles.Systems;

/// <summary>
/// Keeps nav map data on any grid carrying a shuttle console, so the console's whole-ship view has
/// something to draw on ships that were never registered as a station.
/// </summary>
public sealed partial class ShuttleNavMapSystem : EntitySystem
{
    [Dependency] private NavMapSystem _navMap = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, MapInitEvent>(OnConsoleMapInit);
    }

    private void OnConsoleMapInit(Entity<ShuttleConsoleComponent> ent, ref MapInitEvent args)
    {
        if (Transform(ent).GridUid is { } grid)
            _navMap.EnsureGridNavMap(grid);
    }
}
