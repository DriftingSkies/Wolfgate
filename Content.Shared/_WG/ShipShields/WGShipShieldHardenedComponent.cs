using Robust.Shared.GameStates;

namespace Content.Shared._WG.ShipShields;

/// <summary>
/// On a ship's grid while its raised field is hardened (the Fortress shield module): absorbed hits heat it less, its
/// guns are silent and clients draw the field gold.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WGShipShieldHardenedComponent : Component
{
    /// <summary>Colour a hardened field takes in the world and on radar.</summary>
    public static readonly Color FieldColor = Color.FromHex("#FFD23F");

    /// <summary>How far a hardened field's colour is pulled toward <see cref="FieldColor"/>, keeping a trace of its health.</summary>
    public const float FieldTint = 0.8f;

    /// <summary>Share of the usual heat an absorbed hit adds.</summary>
    [DataField, AutoNetworkedField]
    public float HitFactor = 0.25f;
}
