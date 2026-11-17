namespace Content.Server._WG.NpcCrew.Components;

/// <summary>Lets a captain suspend its crew's course during an alert.</summary>
[RegisterComponent]
public sealed partial class WGCaptainComponent : Component
{
    /// <summary>Evade ship threats or hold for boarders, then resume the interrupted course on all-clear.</summary>
    [DataField]
    public bool HeaveTo = true;
}
