using Content.Client._WG.Cockpit;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Shuttles;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.Shuttles.UI;

public sealed partial class CollisionWarningBanner
{
    [Dependency] private IConfigurationManager _wgTcasConfiguration = default!;

    /// <summary>Shares the banner's assigned grid without depending on its visibility or animation polling.</summary>
    public WGCockpitTcasReading WgCockpitTcasReading()
    {
        var available = _grid is { } grid &&
            _entManager.TryGetComponent<MetaDataComponent>(grid, out var metadata) &&
            metadata.EntityLifeStage < EntityLifeStage.Terminating &&
            (metadata.Flags & MetaDataFlags.Detached) == 0 &&
            _entManager.HasComponent<MapGridComponent>(grid);
        var enabled = _wgTcasConfiguration.GetCVar(CollisionWarningCVars.Enabled) &&
            !_entManager.HasComponent<CollisionWarningDisabledComponent>(_grid);
        _entManager.TryGetComponent<CollisionWarningComponent>(_grid, out var warning);
        return WGCockpitTcasReading.From(available, enabled, warning);
    }
}
