using Content.Server._WG.Encounters.Systems;
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>The raider grid whose docking clamps hold a port's joint, for the docking screen.</summary>
    private NetEntity? WgClampedBy(Entity<DockingComponent> dock)
    {
        return EntityManager.System<WGDockingClampSystem>().Holder(dock) is { } raider ? GetNetEntity(raider) : null;
    }
}
