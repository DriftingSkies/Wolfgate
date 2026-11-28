using Robust.Shared.Input;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipShields;

/// <summary>The shield and vent hotkeys, bound in the pilot's shuttle input context.</summary>
[KeyFunctions]
public static class WGShipShieldKeys
{
    /// <summary>Raises or lowers the flown ship's shields, as the helm's shield switch does.</summary>
    public static readonly BoundKeyFunction WGToggleShipShields = "WGToggleShipShields";

    /// <summary>Vents the flown ship's heat, as the helm's vent button does.</summary>
    public static readonly BoundKeyFunction WGVentShipHeat = "WGVentShipHeat";
}

/// <summary>Sent by a pilot pressing the shield hotkey; the server flips the shields of the ship their helm flies.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldToggleEvent : EntityEventArgs;

/// <summary>Sent by a pilot pressing the vent hotkey; the server vents the ship their helm flies.</summary>
[Serializable, NetSerializable]
public sealed class WGShipVentKeyEvent : EntityEventArgs;
