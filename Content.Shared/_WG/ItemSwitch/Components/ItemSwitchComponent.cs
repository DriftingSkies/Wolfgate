using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.ItemSwitch.Components;

/// <summary>Lets an item switch between appearance states, such as a cloak and a mantle.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ItemSwitchComponent : Component
{
    /// <summary>The item's current state key.</summary>
    [DataField(required: true), AutoNetworkedField]
    public string State = string.Empty;

    [DataField(readOnly: true)]
    public Dictionary<string, ItemSwitchState> States = new();

    /// <summary>Whether the item can be switched by activating it in the world.</summary>
    [DataField]
    public bool OnActivate = true;

    /// <summary>Whether the item can be switched by using it in hand.</summary>
    [DataField]
    public bool OnUse = true;

    [DataField]
    public bool Predictable = true;

    /// <summary>Whether the current state is shown in the item status UI.</summary>
    [DataField]
    public bool ShowLabel;
}

/// <summary>One appearance state of an <see cref="ItemSwitchComponent"/>.</summary>
[DataDefinition]
public sealed partial class ItemSwitchState
{
    [DataField]
    public string? Verb;

    [DataField]
    public SoundSpecifier? SoundStateActivate;

    [DataField]
    public SoundSpecifier? SoundFailToActivate;

    [DataField]
    public ComponentRegistry? Components;

    [DataField]
    public bool RemoveComponents = true;

    [DataField]
    public SpriteSpecifier? Sprite;

    [DataField]
    public bool Hidden;
}

/// <summary>Raised on an entity before its item switch changes state.</summary>
[ByRefEvent]
public record struct ItemSwitchAttemptEvent()
{
    public bool Cancelled;
    public required EntityUid? User { get; init; }
    public required string State { get; init; }
    public string? Popup { get; set; }
}

/// <summary>Raised on an entity after its item switch changes state.</summary>
[ByRefEvent]
public readonly record struct ItemSwitchedEvent
{
    public required bool Predicted { get; init; }
    public required string State { get; init; }
    public required EntityUid? User { get; init; }
}
