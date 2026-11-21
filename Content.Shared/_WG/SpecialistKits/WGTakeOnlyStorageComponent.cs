using Robust.Shared.GameStates;

namespace Content.Shared._WG.SpecialistKits;

/// <summary>
/// A specialist kit's case: items come out but never go back in, and it is deleted once empty. Its storage fill goes
/// in before it seals.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGTakeOnlyStorageComponent : Component
{
    /// <summary>Set once the storage fill is in; from then on nothing can be inserted.</summary>
    [DataField, AutoNetworkedField]
    public bool Sealed;
}
