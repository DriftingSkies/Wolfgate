using Content.Shared._WG.AsteroidMining;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.AsteroidMining;

/// <summary>
/// A worldgen marker that fills its mass budget with asteroids of one composition, then removes itself.
/// It waits a tick so worldgen has handed it to a chunk, which its asteroids then join.
/// </summary>
[RegisterComponent]
public sealed partial class WGAsteroidClusterComponent : Component
{
    [DataField(required: true)] public ProtoId<WGAsteroidCompositionPrototype> Composition;

    /// <summary>
    /// A rich kind that each rock rolls instead, at <see cref="RareChance"/> at the sector centre plus
    /// <see cref="RareChancePerKm"/> for each kilometre out, the distance worldgen picks zones by.
    /// </summary>
    [DataField] public ProtoId<WGAsteroidCompositionPrototype>? RareComposition;
    [DataField] public float RareChance;
    [DataField] public float RareChancePerKm;

    /// <summary>Total rock mass to spawn, in ore veins including inert rock. Worldgen replaces it per swapped asteroid.</summary>
    [DataField] public float Mass = 300f;

    /// <summary>Asteroid grids to fill the budget with, by weight.</summary>
    [DataField(required: true)] public Dictionary<EntProtoId, float> Sizes = new();

    /// <summary>Rocks per cluster at most. Each rock is its own grid, which every player is sent.</summary>
    [DataField] public int MaxRocks = 12;
}
