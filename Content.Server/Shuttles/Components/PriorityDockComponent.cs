using Content.Shared.Tag;
using Content.Shared._WF.EngineCompat;

namespace Content.Server.Shuttles.Components;

/// <summary>
/// Given priority when considering where to dock.
/// </summary>
[RegisterComponent]
public sealed partial class PriorityDockComponent : Component
{
    /// <summary>
    /// Tag to match on the docking request, if this dock is to be prioritised.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite),
     DataField("tag", customTypeSerializer: typeof(PrototypeIdSerializer<TagPrototype>))]
    public string? Tag;
}
