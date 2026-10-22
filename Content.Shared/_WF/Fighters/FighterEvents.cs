using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Fighters;

[Serializable, NetSerializable]
public sealed partial class FighterEnterDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class FighterExitDoAfterEvent : SimpleDoAfterEvent;

public sealed partial class FighterExitActionEvent : InstantActionEvent;

public sealed partial class FighterFollowCursorActionEvent : InstantActionEvent;

public sealed partial class FighterCockpitActionEvent : InstantActionEvent;

/// <summary>Where the pilot's cursor is, as a world rotation from the fighter. Sent while cursor follow is on.</summary>
[Serializable, NetSerializable]
public sealed class FighterCursorAimEvent : EntityEventArgs
{
    public NetEntity Fighter;
    public Angle Angle;
}

/// <summary>The cockpit's follow-cursor switch.</summary>
[Serializable, NetSerializable]
public sealed class FighterCockpitFollowCursorMessage : BoundUserInterfaceMessage;

/// <summary>The cockpit's leave-fighter switch.</summary>
[Serializable, NetSerializable]
public sealed class FighterCockpitLeaveMessage : BoundUserInterfaceMessage;

/// <summary>The cockpit's cruise, drive and park switches.</summary>
[Serializable, NetSerializable]
public sealed class FighterCockpitFlightModeMessage(FighterFlightMode mode) : BoundUserInterfaceMessage
{
    public readonly FighterFlightMode Mode = mode;
}
