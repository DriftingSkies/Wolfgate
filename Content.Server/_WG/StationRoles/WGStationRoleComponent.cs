using Robust.Shared.Network;

namespace Content.Server._WG.StationRoles;

/// <summary>On a body spawned into a salaried station job; tracks duty time towards the next payday.</summary>
[RegisterComponent, Access(typeof(WGStationSalarySystem))]
public sealed partial class WGStationRoleComponent : Component
{
    /// <summary>The player the salary belongs to. Anyone else in this body earns nothing.</summary>
    [ViewVariables]
    public NetUserId User;

    /// <summary>The job, and the ID of its salary.</summary>
    [ViewVariables]
    public string Job = string.Empty;

    /// <summary>The character slot whose bank is paid.</summary>
    [ViewVariables]
    public int Slot;

    /// <summary>Duty seconds since the last payday.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public int UnpaidSeconds;
}
