namespace Content.Server._WG.Companies;

/// <summary>Tracks which radio parts the company channel added to a body, so it removes only those.</summary>
[RegisterComponent]
public sealed partial class WGCompanyRadioComponent : Component
{
    public bool AddedTransmitter;
    public bool AddedReceiver;
    public bool AddedActive;
}
