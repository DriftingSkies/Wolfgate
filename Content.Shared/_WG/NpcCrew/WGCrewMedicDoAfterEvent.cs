using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.NpcCrew;

/// <summary>An NPC medic's treatment in progress; what it does is kept on the medic, which only the server reads.</summary>
[Serializable, NetSerializable]
public sealed partial class WGCrewMedicDoAfterEvent : SimpleDoAfterEvent;
