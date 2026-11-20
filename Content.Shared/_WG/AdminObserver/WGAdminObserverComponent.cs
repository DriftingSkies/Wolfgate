using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.AdminObserver;

/// <summary>Marks the admin observer and tracks whose kit it carries. The fields are the server's.</summary>
[RegisterComponent]
public sealed partial class WGAdminObserverComponent : Component
{
    /// <summary>The player whose kit this observer was given.</summary>
    [ViewVariables]
    public NetUserId? Owner;

    /// <summary>Whether the kit has been handed out; it is once per observer.</summary>
    [ViewVariables]
    public bool KitApplied;

    /// <summary>Whether what this observer carries is written back to <see cref="Owner"/>'s saved layout.</summary>
    [ViewVariables]
    public bool SavesKit;

    /// <summary>Whether items outside the kit catalogue are saved too: the owner holds the Spawn flag.</summary>
    [ViewVariables]
    public bool SavesAnything;

    /// <summary>Whether the layout was saved when the player last left, so deletion need not save it again.</summary>
    [ViewVariables]
    public bool SavedOnLeave;

    /// <summary>The action that shows the observer to players and hides it again.</summary>
    [DataField]
    public EntProtoId ManifestAction = "WGActionManifest";

    [ViewVariables]
    public EntityUid? ManifestActionEntity;

    /// <summary>The action that opens the observer panel.</summary>
    [DataField]
    public EntProtoId PanelAction = "WGActionObserverPanel";

    [ViewVariables]
    public EntityUid? PanelActionEntity;

    /// <summary>The humanoid carried for the last character avatar, kept so it can be dressed while hidden.</summary>
    [ViewVariables]
    public EntityUid? AvatarBody;
}
