using Content.Shared.Actions;

namespace Content.Shared._WG.Remade.Signals;

/// <summary>A Signal plays a trick on whatever it points at.</summary>
public sealed partial class WGSignalTrickEvent : EntityTargetActionEvent
{
    [DataField(required: true)]
    public WGSignalTrick Trick;
}

/// <summary>A Signal takes over the Remade it points at.</summary>
public sealed partial class WGSignalPossessEvent : EntityTargetActionEvent
{
}

/// <summary>A Signal driving a Remade lets go of it.</summary>
public sealed partial class WGSignalReleaseEvent : InstantActionEvent
{
}

/// <summary>A driven Remade uses one of its abilities at a point. <see cref="Ability"/> is the ability's class name.</summary>
public sealed partial class WGSignalAbilityEvent : WorldTargetActionEvent
{
    [DataField(required: true)]
    public string Ability = string.Empty;
}
