using Robust.Shared.Serialization;

namespace Content.Shared._WF.Fighters;

/// <summary>Which thrusters are burning, each a bool the sprite shows a layer for.</summary>
[Serializable, NetSerializable]
public enum FighterVisuals : byte
{
    MainBurn,
    RetroBurn,
    PortBurn,
    StarboardBurn,
}

[Serializable, NetSerializable]
public enum FighterUiKey : byte
{
    Cockpit,
}

/// <summary>How the fighter treats motion nobody is thrusting for, after the ship console's dampener modes.</summary>
[Serializable, NetSerializable]
public enum FighterFlightMode : byte
{
    /// <summary>Keeps every bit of velocity it is given.</summary>
    Cruise,

    /// <summary>Bleeds off motion along any axis without thrust.</summary>
    Drive,

    /// <summary>Ignores thrust and brakes to a standstill.</summary>
    Park,
}
