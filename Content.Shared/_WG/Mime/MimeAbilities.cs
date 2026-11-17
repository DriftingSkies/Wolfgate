using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Mime;

/// <summary>Grants the mime's actions.</summary>
[RegisterComponent]
public sealed partial class MimeAbilitiesComponent : Component
{
    /// <summary>Action prototypes to grant.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Actions = new();

    /// <summary>Granted action entities.</summary>
    public List<EntityUid> Granted = new();
}

/// <summary>Goes on a mime action. The item stays in a container on the action between summons.</summary>
[RegisterComponent]
public sealed partial class MimeSummonActionComponent : Component
{
    /// <summary>Item the action summons.</summary>
    [DataField(required: true)]
    public EntProtoId ItemId;

    /// <summary>Message prefixes. The code adds -self and -others.</summary>
    [DataField(required: true)]
    public string SummonMessage = string.Empty;

    /// <summary>Message prefix for putting the item away, completed like <see cref="SummonMessage"/>.</summary>
    [DataField(required: true)]
    public string PutAwayMessage = string.Empty;

    /// <summary>The summoned item, held or in the action's container.</summary>
    [DataField]
    public EntityUid? Item;

    /// <summary>Container on the action that holds the item while it is put away.</summary>
    public const string ContainerId = "wg-mime-summon-item";
}

/// <summary>Raised by a mime summon action to summon or put away its item.</summary>
[ByRefEvent]
public sealed partial class MimeSummonActionEvent : InstantActionEvent;

/// <summary>Marks the invisible box, so others see items go in and out.</summary>
[RegisterComponent]
public sealed partial class InvisibleBoxComponent : Component;
