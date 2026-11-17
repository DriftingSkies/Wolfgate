using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class RadarConsoleSystem
{
    [Dependency] private MetaDataSystem _wgMeta = default!;

    /// <summary>An open scanner re-picks its origin when carried on to another map, such as a vehicle's interior.</summary>
    private void OnWgMapChanged(Entity<RadarConsoleComponent> ent, ref MapUidChangedEvent args)
    {
        if (_uiSystem.IsUiOpen(ent.Owner, RadarConsoleUiKey.Key))
            UpdateState(ent, ent.Comp);
    }
}
