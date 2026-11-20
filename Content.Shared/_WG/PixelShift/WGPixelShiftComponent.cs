using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.PixelShift;

/// <summary>A mob's own sprite shift and tilt, seen by everyone. Kept until reset or a step.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGPixelShiftComponent : Component
{
    /// <summary>Screen-aligned shift in pixels.</summary>
    [DataField, AutoNetworkedField]
    public Vector2i Offset;

    /// <summary>Tilt in degrees; positive leans left.</summary>
    [DataField, AutoNetworkedField]
    public int Tilt;

    /// <summary>Client: the shift shown, in pixels, gliding toward <see cref="Offset"/>.</summary>
    [ViewVariables]
    public Vector2 ShownOffset;

    /// <summary>Client: the tilt shown, in degrees, gliding toward <see cref="Tilt"/>.</summary>
    [ViewVariables]
    public float ShownTilt;

    /// <summary>Client: the shift added to the sprite this frame.</summary>
    [ViewVariables]
    public Vector2 AppliedOffset;

    /// <summary>Client: the tilt added to the sprite this frame.</summary>
    [ViewVariables]
    public Angle AppliedTilt;

    /// <summary>Client: the sprite offset after the shift was added, to tell whether something has set it since.</summary>
    [ViewVariables]
    public Vector2 WrittenOffset;

    /// <summary>Client: the sprite rotation after the tilt was added.</summary>
    [ViewVariables]
    public Angle WrittenRotation;
}

/// <summary>Which pixel shift keys an entity's player is holding. Not networked, like the turn key.</summary>
[RegisterComponent]
public sealed partial class WGPixelShiftHeldComponent : Component
{
    [ViewVariables]
    public bool Shift;

    [ViewVariables]
    public bool Tilt;

    /// <summary>What movement keys do: the latest of the held keys.</summary>
    [ViewVariables]
    public WGPixelShiftMode Mode;

    /// <summary>Movement keys pressed while a shift key is held and not yet let go; they keep nudging.</summary>
    [ViewVariables]
    public DirectionFlag Directions;

    /// <summary>Server: the part of a pixel or degree the held movement keys have built up toward their next nudge.</summary>
    [ViewVariables]
    public float Progress;
}

public enum WGPixelShiftMode : byte
{
    Shift,
    Tilt,
}
