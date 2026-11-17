using Robust.Shared.Serialization;

namespace Content.Shared._WG.NpcCrew;

/// <summary>When a ship's NPC gunners open fire without being given a target.</summary>
[Serializable, NetSerializable]
public enum WGGunnerPolicy : byte
{
    /// <summary>On any ship the crew has marked hostile: one that fired on it, broke a zone's rule or docked unwelcome.</summary>
    Hostiles,

    /// <summary>Only on ships that have fired on this one or on a ship docked to it.</summary>
    DefendDocked,

    /// <summary>Only on ships that have fired on this one.</summary>
    WhenFiredOn,
}
