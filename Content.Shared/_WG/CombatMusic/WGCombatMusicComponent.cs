using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.CombatMusic;

/// <summary>On a player's body: the combat playlist from the character they spawned as. Only sent to its owner.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGCombatMusicComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public List<ProtoId<WGCombatTrackPrototype>> Tracks = new();
}
