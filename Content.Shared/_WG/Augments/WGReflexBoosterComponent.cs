using Robust.Shared.GameStates;

namespace Content.Shared._WG.Augments;

/// <summary>A reflex booster in the head: its body flies ships better and slips the odd bullet, but feels more pain.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGReflexBoosterComponent : Component
{
    /// <summary>What a ship's thrust and turning are multiplied by while this body flies it.</summary>
    [DataField]
    public float ShipHandling = 1.15f;

    /// <summary>The chance that a bullet or bolt about to hit the body misses it.</summary>
    [DataField]
    public float DodgeChance = 0.05f;

    /// <summary>What every pain gain is multiplied by.</summary>
    [DataField]
    public float PainMultiplier = 1.05f;
}
