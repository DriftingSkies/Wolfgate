using Content.Shared._WG.NpcCrew;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>Configured responses to unauthorized visitors and arriving ships.</summary>
[RegisterComponent]
public sealed partial class WGCrewSecurityComponent : Component
{
    [DataField]
    public WGCrewSecurityResponse Boarding = WGCrewSecurityResponse.Hostile;

    [DataField]
    public WGCrewSecurityResponse Docking = WGCrewSecurityResponse.Hostile;

    /// <summary>Warn boarding: how long a stranger the crew has noticed may stay aboard before its fighters turn on him.</summary>
    [DataField]
    public TimeSpan WarnTime = TimeSpan.FromSeconds(30);
}
