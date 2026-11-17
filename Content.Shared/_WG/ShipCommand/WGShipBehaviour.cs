using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipCommand;

/// <summary>Where a commanded ship's marines stand when nothing is happening.</summary>
[Serializable, NetSerializable]
public enum WGShipMarineOrders : byte
{
    /// <summary>They wander the deck between spots the ship's layout gives.</summary>
    Patrol,

    /// <summary>They walk the ship's marine posts in turn.</summary>
    Rounds,

    /// <summary>Each stays at his own post.</summary>
    Posts,
}

/// <summary>What a commanded ship's pilot does when another ship attacks it.</summary>
[Serializable, NetSerializable]
public enum WGShipPilotStance : byte
{
    /// <summary>Evades, circling the attacker.</summary>
    Evade,

    /// <summary>Flies directly away from the attacker.</summary>
    Run,

    /// <summary>Keeps station and doesn't turn.</summary>
    Hold,

    /// <summary>Keeps station and turns the best armed side to the attacker.</summary>
    FaceThreats,
}
