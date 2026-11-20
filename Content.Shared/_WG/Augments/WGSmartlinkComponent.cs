using Robust.Shared.GameStates;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A gun link in a hand that compensates for recoil: a gun held in it kicks the view less, spreads slower as it fires,
/// settles sooner and fires faster rounds, and kicks and spreads more while an EMP has it scrambled. Its tightest and
/// widest spread stay the gun's own.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGSmartlinkComponent : Component
{
    /// <summary>What the spread each shot adds is multiplied by.</summary>
    [DataField]
    public float IncreaseMultiplier = 0.5f;

    /// <summary>What the rate the spread settles at is multiplied by.</summary>
    [DataField]
    public float DecayMultiplier = 1.5f;

    /// <summary>What the spread each shot adds is multiplied by while scrambled.</summary>
    [DataField]
    public float ScrambledIncreaseMultiplier = 1.5f;

    /// <summary>What the rate the spread settles at is multiplied by while scrambled.</summary>
    [DataField]
    public float ScrambledDecayMultiplier = 0.67f;

    /// <summary>What the gun's camera kick is multiplied by.</summary>
    [DataField]
    public float CameraRecoilMultiplier = 0.35f;

    /// <summary>What the gun's camera kick is multiplied by while scrambled.</summary>
    [DataField]
    public float ScrambledCameraRecoilMultiplier = 1.5f;

    /// <summary>What the speed of the gun's rounds is multiplied by; a scrambled link leaves it alone.</summary>
    [DataField]
    public float ProjectileSpeedMultiplier = 1.2f;

    /// <summary>Set by an EMP until it wears off.</summary>
    [DataField, AutoNetworkedField]
    public bool Scrambled;
}

/// <summary>On a body with a smartlinked hand, so the guns it holds ask the link for their spread.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGSmartlinkUserComponent : Component;
