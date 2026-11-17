using Content.Shared.Guidebook;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.LoreBook;

/// <summary>
/// Gives entities a lore book button on examine that opens <see cref="Entry"/>. Prototypes that inherit from a listed
/// entity get it too, unless a closer link names them.
/// </summary>
[Prototype("wgLoreLink")]
public sealed partial class WGLoreLinkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The lore book page to open.</summary>
    [DataField(required: true)]
    public ProtoId<GuideEntryPrototype> Entry;

    /// <summary>Entity prototypes that get the button, with everything that inherits from them.</summary>
    [DataField]
    public List<EntProtoId> Entities = new();
}
