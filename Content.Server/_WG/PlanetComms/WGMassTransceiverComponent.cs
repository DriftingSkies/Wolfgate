namespace Content.Server._WG.PlanetComms;

/// <summary>A mass transceiver: while powered and whole, it carries the comms of the world it stands on.</summary>
[RegisterComponent, Access(typeof(WGPlanetCommsSystem))]
public sealed partial class WGMassTransceiverComponent : Component
{
    /// <summary>A wreck: never carries comms, whatever its power.</summary>
    [DataField]
    public bool Broken;
}
