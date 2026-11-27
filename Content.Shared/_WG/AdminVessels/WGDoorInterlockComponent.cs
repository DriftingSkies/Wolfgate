using Robust.Shared.GameStates;

namespace Content.Shared._WG.AdminVessels;

/// <summary>
/// One door of an interlocked pair, such as a Lupercal core gate: it won't open unless its partner is closed, and asked
/// to open while the partner stands open it closes the partner first and then opens. Unlike bolting each other through
/// device links, the pair can never hold each other open.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGDoorInterlockComponent : Component
{
    /// <summary>The other door of the pair.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Partner;

    /// <summary>Until when this door waits for its partner to shut before it opens. Server only.</summary>
    [ViewVariables]
    public TimeSpan CycleUntil;

    /// <summary>Who asked this door to open, so it opens with their access once the partner shuts.</summary>
    [ViewVariables]
    public EntityUid? CycleUser;

    /// <summary>Close as soon as this door is fully open: its partner wants to cycle. Server only.</summary>
    [ViewVariables]
    public bool CloseWhenOpen;
}
