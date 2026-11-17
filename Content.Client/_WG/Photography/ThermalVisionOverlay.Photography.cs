namespace Content.Client._White.Overlays;

public sealed partial class ThermalVisionOverlay
{
    /// <summary>The light thermal vision puts on its viewer, so photos can leave it out.</summary>
    public EntityUid? LightEntity => _lightEntity;
}
