using Robust.Shared.Audio;

namespace Content.Server._WF.OutpostRaids;

/// <summary>Tracks raid shuttles descending on its outpost, so its batteries can fire on them.</summary>
[RegisterComponent]
public sealed partial class WFSensorUplinkComponent : Component;

/// <summary>Fires on a raid shuttle descending on its outpost while a sensor uplink tracks it. Never on anything else.</summary>
[RegisterComponent]
public sealed partial class WFOrbitalBatteryComponent : Component
{
    /// <summary>Hull taken off the shuttle each salvo.</summary>
    [DataField]
    public float Damage = 15f;

    [DataField]
    public SoundSpecifier? Sound;
}

/// <summary>Scrambles a raid shuttle's descent on its outpost, so it spends more salvos under the batteries.</summary>
[RegisterComponent]
public sealed partial class WFDescentJammerComponent : Component
{
    [DataField]
    public int ExtraSalvos = 2;
}
