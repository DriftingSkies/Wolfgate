using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Cyborgs;

/// <summary>
/// A cyborg whose encryption keys add channels to its built-in ones. The key slots open while the chassis lock is open.
/// </summary>
[RegisterComponent]
public sealed partial class WGBorgRadioKeysComponent : Component
{
    /// <summary>
    /// The channels the cyborg transmits on without keys: its prototype's, then its type's once one is picked.
    /// </summary>
    [ViewVariables]
    public HashSet<ProtoId<RadioChannelPrototype>> BaseTransmit = new();

    /// <summary>
    /// The channels the cyborg hears without keys.
    /// </summary>
    [ViewVariables]
    public HashSet<ProtoId<RadioChannelPrototype>> BaseReceive = new();
}
