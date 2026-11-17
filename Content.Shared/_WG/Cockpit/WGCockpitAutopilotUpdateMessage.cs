using Robust.Shared.Serialization;

namespace Content.Shared._WG.Cockpit;

/// <summary>Updates the autopilot lamp without replacing the current map or flight controls.</summary>
[Serializable, NetSerializable]
public sealed class WGCockpitAutopilotUpdateMessage(bool? active) : BoundUserInterfaceMessage
{
    /// <summary>Null denotes a console without an available autopilot.</summary>
    public bool? Active = active;
}
