using Robust.Shared.Audio;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Fortress shield: a switch that hardens the ship's raised field. Hits it absorbs heat the ship far less, but the
/// field builds hard heat every second it is on and the ship's guns are silent.
/// </summary>
public sealed partial class WGFortressShieldEffect : WGShipModuleEffect
{
    /// <summary>Share of the usual heat an absorbed hit adds while it is on.</summary>
    [DataField]
    public float HitFactor = 0.25f;

    /// <summary>Share of the ship's heat capacity it adds each second it is on, as hard heat.</summary>
    [DataField]
    public float HeatPerSecond = 0.04f;

    /// <summary>Loop the crew hears while it is on, faded in under the switch-on sound.</summary>
    [DataField]
    public SoundSpecifier? Hum;

    /// <summary>Time after switching on that <see cref="Hum"/> starts to fade in.</summary>
    [DataField]
    public TimeSpan HumDelay = TimeSpan.FromSeconds(4.5);

    /// <summary>Time <see cref="Hum"/> takes to reach full volume.</summary>
    [DataField]
    public TimeSpan HumFade = TimeSpan.FromSeconds(1.5);
}
