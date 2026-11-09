using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Outposts;

/// <summary>
/// A trade delivery coming down under canopy onto its drop tile. The client draws the product sinking from overhead;
/// the server swaps it for the product on touchdown.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFTradeDropComponent : Component
{
    /// <summary>What lands; the descent prototype leaves it to whoever spawns the drop.</summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? Product;

    /// <summary>When the descent began.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Start;

    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);

    /// <summary>How high over the tile the descent starts, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float Height = 9f;

    [DataField]
    public SoundSpecifier LandSound = new SoundCollectionSpecifier("MetalThud");

    /// <summary>Kicked up where it lands.</summary>
    [DataField]
    public EntProtoId LandEffect = "CEDustEffect";
}
