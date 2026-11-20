namespace Content.Client._WG.WallmountFov;

/// <summary>
/// Marks a wall mount whose sprite offset draws it on the wall tile, so <see cref="WGWallBackfaceSystem"/> hides it
/// from eyes behind that wall. The offset gives the wall's direction; a child that draws on its own tile is left alone.
/// </summary>
/// <remarks>Client only; the server ignores it by its <c>Visuals</c> suffix.</remarks>
[RegisterComponent]
public sealed partial class WGWallBackfaceVisualsComponent : Component;
