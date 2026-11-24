using Robust.Shared.Serialization;

namespace Content.Shared._WG.PdaUi;

/// <summary>Asks the PDA to toggle chat notifications from its programs.</summary>
[Serializable, NetSerializable]
public sealed class PdaToggleNotificationsMessage : BoundUserInterfaceMessage
{
    public PdaToggleNotificationsMessage() { }
}
