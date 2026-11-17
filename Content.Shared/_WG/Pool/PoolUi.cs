using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Pool;

[Serializable, NetSerializable]
public enum WGPoolUiKey : byte
{
    Key,
}

/// <summary>Shoot the cue ball: the direction in radians and the power from zero to one.</summary>
[Serializable, NetSerializable]
public sealed class PoolShootMessage : BoundUserInterfaceMessage
{
    public float Angle;
    public float Power;

    public PoolShootMessage(float angle, float power)
    {
        Angle = angle;
        Power = power;
    }
}

/// <summary>Put the cue ball down while it is in hand.</summary>
[Serializable, NetSerializable]
public sealed class PoolPlaceCueMessage : BoundUserInterfaceMessage
{
    public Vector2 Position;

    public PoolPlaceCueMessage(Vector2 position)
    {
        Position = position;
    }
}

/// <summary>Take a side of the table, or leave it with -1.</summary>
[Serializable, NetSerializable]
public sealed class PoolSeatMessage : BoundUserInterfaceMessage
{
    public int Seat;

    public PoolSeatMessage(int seat)
    {
        Seat = seat;
    }
}

/// <summary>Rack the balls for a new game.</summary>
[Serializable, NetSerializable]
public sealed class PoolRerackMessage : BoundUserInterfaceMessage;

/// <summary>The settled table, the players, and the last shot for the client to replay.</summary>
[Serializable, NetSerializable]
public sealed class PoolUiState : BoundUserInterfaceState
{
    public Vector2[] Balls = Array.Empty<Vector2>();
    public ushort Down;
    public int Turn;
    public PoolGroup[] Groups = Array.Empty<PoolGroup>();
    public bool BallInHand;
    public bool Breaking;
    public int Winner;
    public PoolOutcome Outcome;

    /// <summary>The player on each side, if any, and the name to show for them.</summary>
    public NetEntity?[] Seats = Array.Empty<NetEntity?>();
    public string?[] SeatNames = Array.Empty<string?>();

    public int ShotId;
    public Vector2[] ShotStart = Array.Empty<Vector2>();
    public ushort ShotStartDown;
    public Vector2 ShotVelocity;
}
