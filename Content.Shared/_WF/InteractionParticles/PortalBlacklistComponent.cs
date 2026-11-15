using Robust.Shared.GameStates;

namespace Content.Shared._WF.InteractionParticles;

/// <summary>
/// Prevents this entity from being teleported by portals.
/// </summary>
/// <remarks>
/// Ported from Starlight's <c>_Moffstation</c> portal blacklist. Portal effects spawn sparks, and those sparks can fly
/// back into the portal and spawn another effect, so the effects and their sparks carry this component.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class PortalBlacklistComponent : Component;
