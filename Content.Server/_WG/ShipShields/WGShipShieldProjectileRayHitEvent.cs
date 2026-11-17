using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._WG.ShipShields;

/// <summary>Lets a shield consume a confirmed fast projectile hit before physical contact.</summary>
[ByRefEvent]
public record struct WGShipShieldProjectileRayHitEvent(EntityUid Projectile, ProjectileComponent Component, MapCoordinates Position, bool Handled = false);
