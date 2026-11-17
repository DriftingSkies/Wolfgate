namespace Content.Server._WG.AdminVessels;

/// <summary>
/// On a grid: no blast breaks its floor tiles, though the blast still hurts what stands on them. The Lupercal's floors;
/// prying, laying and the RCD are held off by its ProtectedGrid.
/// </summary>
[RegisterComponent]
public sealed partial class WGBlastproofFloorsComponent : Component;
