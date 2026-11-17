namespace Content.Server._WG.GridSettle;

/// <summary>Marks a grid nobody is flying or aboard, so space friction brings it to rest and it stops networking.</summary>
[RegisterComponent, Access(typeof(WGGridSettleSystem))]
public sealed partial class WGGridSettleComponent : Component;
