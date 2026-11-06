using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Picks a new projectile from <see cref="Prototypes"/> for the mob's basic entity ammo after every shot.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaRandomAmmoComponent : Component
{
    /// <summary>
    /// Projectiles picked from at random after each shot.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Prototypes = new();
}
