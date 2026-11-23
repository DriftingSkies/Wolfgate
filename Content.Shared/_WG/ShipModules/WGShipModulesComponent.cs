using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// A ship's fitted modules as its helm shows them, kept on the grid by the server. Absent on a ship with none.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WGShipModulesComponent : Component
{
    /// <summary>Module slots every ship has, one per hotkey.</summary>
    public const int SlotCount = 3;

    /// <summary>The ship's fitted modules in slot order; empty slots are left out.</summary>
    [ViewVariables, AutoNetworkedField]
    public List<WGShipModuleSlot> Slots = new();
}

/// <summary>One fitted module on the helm's strip.</summary>
[Serializable, NetSerializable]
public record struct WGShipModuleSlot(
    int Slot,
    ProtoId<WGShipModulePrototype> Module,
    int Charges,
    int MaxCharges,
    TimeSpan NextCharge,
    TimeSpan ReadyAt,
    WGShipModuleLock Lock);

/// <summary>Why a module can't be fired right now, whatever its charges.</summary>
[Serializable, NetSerializable]
public enum WGShipModuleLock : byte
{
    None,
    Unpowered,
    Shields,
    Ftl,
    Docked,
    Anchored,
}

/// <summary>A pilot asks to fire one module slot of the ship they are flying.</summary>
[Serializable, NetSerializable]
public sealed class WGShipModuleActivateEvent : EntityEventArgs
{
    /// <summary>Slot index, from 0.</summary>
    public int Slot;

    public WGShipModuleActivateEvent(int slot)
    {
        Slot = slot;
    }
}

/// <summary>A module fired: helms showing its ship light its key and shake, and the crew's view jolts.</summary>
[Serializable, NetSerializable]
public sealed class WGShipModuleFiredEvent : EntityEventArgs
{
    public NetEntity Ship;
    public int Slot;
    public float Jolt;

    public WGShipModuleFiredEvent(NetEntity ship, int slot, float jolt)
    {
        Ship = ship;
        Slot = slot;
        Jolt = jolt;
    }
}
