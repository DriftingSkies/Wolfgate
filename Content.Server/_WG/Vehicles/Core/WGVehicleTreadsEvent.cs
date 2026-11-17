using System.Numerics;

namespace Content.Server._WG.Vehicles.Core;

/// <summary>Raised when a moving vehicle lays a pair of tread marks, with where its treads are in its grid's space.</summary>
[ByRefEvent]
public readonly record struct WGVehicleTreadsEvent(EntityUid Vehicle, EntityUid Grid, Vector2 Left, Vector2 Right);
