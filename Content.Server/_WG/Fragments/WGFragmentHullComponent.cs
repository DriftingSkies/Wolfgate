namespace Content.Server._WG.Fragments;

/// <summary>
/// A wall, door or window of a landed fragment: takes no damage and stops blasts, so crews board through its openings
/// instead of shooting their way in. Doors still open and pry as usual.
/// </summary>
[RegisterComponent]
public sealed partial class WGFragmentHullComponent : Component;
