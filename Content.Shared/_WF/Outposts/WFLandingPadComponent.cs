using Robust.Shared.GameStates;

namespace Content.Shared._WF.Outposts;

/// <summary>
/// An outpost's landing pad beacon: a square of ground centred on it where a pilot with access lands from orbit and
/// lifts off again, guided down and up whatever the hull's lift.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFLandingPadComponent : Component
{
    public const int MinSize = 5;
    public const int MaxSize = 21;

    /// <summary>The side of the pad's square in tiles, odd so the beacon sits in its middle.</summary>
    [DataField, AutoNetworkedField]
    public int Size = 9;

    /// <summary>The widest its builders may set it.</summary>
    [DataField, AutoNetworkedField]
    public int Widest = MaxSize;

    /// <summary>True when any pilot may land on it, not only the outpost's owner and allow list.</summary>
    [DataField, AutoNetworkedField]
    public bool Public;

    /// <summary>True while it guides a hull down or up.</summary>
    [DataField, AutoNetworkedField]
    public bool Guiding;

    /// <summary>The beacon's draw while idle, in watts.</summary>
    [DataField]
    public float IdleLoad = 100f;

    /// <summary>The beacon's draw while guiding, in watts.</summary>
    [DataField]
    public float GuidingLoad = 5000f;

    /// <summary>The hull parked on the pad.</summary>
    [ViewVariables]
    public EntityUid? Parked;
}
