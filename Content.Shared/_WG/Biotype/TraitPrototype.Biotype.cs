using Content.Shared._WG.Biotype;

namespace Content.Shared.Traits;

public sealed partial class TraitPrototype
{
    /// <summary>Biotypes that can't take this trait, whatever the species: a machine body has no blood to thin.</summary>
    [DataField]
    public HashSet<WGBiotype> BiotypeBlacklist { get; private set; } = new();
}
