using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Pyramid;

/// <summary>Marks the Pyramid for its client effects: fragments, the reality field, its flaring aura.</summary>
[RegisterComponent]
public sealed partial class WGPyramidVisualsComponent : Component;

/// <summary>A barrage mark on a hull; the lasers aim at it when they fire, and the client draws a tether to it.</summary>
[RegisterComponent]
public sealed partial class WGPyramidMarkComponent : Component;

/// <summary>A wave of warped space leaving the Pyramid, for the screens of everyone on its map.</summary>
[Serializable, NetSerializable]
public sealed class WGPyramidWaveEvent(NetEntity source, MapId map, Vector2 origin, float speed, float reach, float strength)
    : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly MapId Map = map;
    public readonly Vector2 Origin = origin;

    /// <summary>How fast the front runs out, in metres a second.</summary>
    public readonly float Speed = speed;

    /// <summary>Where it dies out, in metres.</summary>
    public readonly float Reach = reach;

    /// <summary>1 for a wave, more for its last one as it collapses.</summary>
    public readonly float Strength = strength;
}

/// <summary>The Pyramid's lasers firing from it into each marked patch of a hull.</summary>
[Serializable, NetSerializable]
public sealed class WGPyramidBeamEvent(MapId map, Vector2 from, List<Vector2> to) : EntityEventArgs
{
    public readonly MapId Map = map;
    public readonly Vector2 From = from;
    public readonly List<Vector2> To = to;
}

/// <summary>The Pyramid has been brought down and is collapsing; it folds away after this many seconds.</summary>
[Serializable, NetSerializable]
public sealed class WGPyramidCollapseEvent(NetEntity pyramid, MapId map, Vector2 at, float seconds) : EntityEventArgs
{
    public readonly NetEntity Pyramid = pyramid;
    public readonly MapId Map = map;
    public readonly Vector2 At = at;
    public readonly float Seconds = seconds;
}

/// <summary>Shared timings and sizes for the Pyramid's effects.</summary>
public static class WGPyramidMath
{
    /// <summary>How wide a wave's front is, in metres.</summary>
    public const float WaveWidth = 14f;

    /// <summary>How long a laser stays on screen, in seconds.</summary>
    public const float BeamSeconds = 0.7f;

    /// <summary>How long the screen warps once a wave passes, in seconds.</summary>
    public const float WarpSeconds = 1.6f;

    /// <summary>How long a laser hit keeps tearing at reality, in seconds.</summary>
    public const float ImpactSeconds = 1.4f;

    /// <summary>How far round the Pyramid reality bends and tears, in metres.</summary>
    public const float FieldRadius = 48f;

    /// <summary>How long a barrage charges, matching the charge sound and the mark's animation.</summary>
    public const float ChargeSeconds = 6f;
}
