namespace Content.Shared._WG.Shuttles;

/// <summary>Raised on a grid when a validated construction operation leaves no floor at a location, so it leaves the ship's design.</summary>
[ByRefEvent]
public readonly record struct WGHullTileDeconstructedEvent(Vector2i Index);
