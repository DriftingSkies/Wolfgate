namespace Content.Server._WG.DeviceNetGuard;

/// <summary>Which mode change an air alarm last applied, so linked alarms agree on the newest one.</summary>
[RegisterComponent]
public sealed partial class AirAlarmModeStampComponent : Component
{
    /// <summary>Game tick the mode was set at.</summary>
    public uint Tick;

    /// <summary>Address of the alarm it was set at; breaks ties within a tick.</summary>
    public string Origin = string.Empty;
}
