using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Medical;

/// <summary>
/// A wheeled stand that breathes for a patient from a gas tank. With its mask on somebody in reach and gas in the
/// tank, their brain stops paying for missing, failing or sedated lungs and for bad air.
/// </summary>
/// <remarks>The gas itself is not checked: with no lungs in the way there is nothing for a wrong mix to poison.</remarks>
[RegisterComponent]
public sealed partial class WolfmedVentilatorComponent : Component
{
    public const string TankSlotId = "wolfmed_ventilator_tank";

    /// <summary>How far the patient may be from the stand before the mask comes off. One tile, diagonals included.</summary>
    [DataField]
    public float Range = 1.5f;

    /// <summary>Seconds between breaths taken from the tank: the respirator's own cycle.</summary>
    [DataField]
    public float Interval = 2f;

    /// <summary>Seconds it takes to fit the mask.</summary>
    [DataField]
    public float AttachSeconds = 2f;

    /// <summary>The least gas, in moles, that counts as a breath. A regulated tank gives about twice this.</summary>
    [DataField]
    public float MinBreathMoles = 0.002f;

    /// <summary>Dry breaths between alarms while the mask is on somebody.</summary>
    [DataField]
    public int AlarmEvery = 3;

    /// <summary>Played with the alarm line while the mask is on and no gas comes.</summary>
    [DataField]
    public SoundSpecifier? AlarmSound = new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");

    // Runtime state, written on the server only.

    /// <summary>Whoever wears the mask.</summary>
    [ViewVariables]
    public EntityUid? Patient;

    /// <summary>The last breath had gas in it.</summary>
    [ViewVariables]
    public bool Supplying;

    /// <summary>Breaths in a row that came up dry.</summary>
    [ViewVariables]
    public int DryBreaths;

    [ViewVariables]
    public TimeSpan NextBreath;
}

/// <summary>Appearance keys the ventilator's sprite reads.</summary>
[Serializable, NetSerializable]
public enum WolfmedVentilatorVisuals : byte
{
    /// <summary>Bool: a tank is fitted.</summary>
    Tank,

    /// <summary>Bool: the mask is on somebody.</summary>
    Attached,
}

/// <summary>The ventilator's sprite layers.</summary>
[Serializable, NetSerializable]
public enum WolfmedVentilatorLayers : byte
{
    Tank,
    Mask,
}

/// <summary>Fitting the ventilator's mask to a patient.</summary>
[Serializable, NetSerializable]
public sealed partial class WolfmedVentilatorAttachDoAfterEvent : SimpleDoAfterEvent;
