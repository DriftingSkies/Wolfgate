namespace Content.Server._WG.RoundSetup;

/// <summary>Tracks only the intrinsic radio components installed by crew membership.</summary>
[RegisterComponent]
public sealed partial class WGPlayerCrewRadioComponent : Component
{
    public bool AddedTransmitter;
    public bool AddedReceiver;
    public bool AddedActive;
}
