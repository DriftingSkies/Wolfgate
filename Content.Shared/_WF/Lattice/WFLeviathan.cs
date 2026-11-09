using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Lattice;

/// <summary>Where the Coda's leviathan is in its pass of a ship.</summary>
[Serializable, NetSerializable]
public enum WFLeviathanPhase : byte
{
    /// <summary>Out at the edge of the radar, circling.</summary>
    Approach,

    /// <summary>Sliding past beneath the ship.</summary>
    Pass,
}

/// <summary>Tells the crew of a ship what the leviathan is doing round it, for their radar and screens.</summary>
[Serializable, NetSerializable]
public sealed class WFLeviathanEvent(NetEntity grid, WFLeviathanPhase phase, float seconds, int seed, Vector2 direction) : EntityEventArgs
{
    public readonly NetEntity Grid = grid;
    public readonly WFLeviathanPhase Phase = phase;
    public readonly float Seconds = seconds;
    public readonly int Seed = seed;

    /// <summary>Which way it travels, in world space.</summary>
    public readonly Vector2 Direction = direction;
}
