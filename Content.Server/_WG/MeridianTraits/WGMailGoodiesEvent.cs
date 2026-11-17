namespace Content.Server._WG.MeridianTraits;

/// <summary>
/// Raised on a mail recipient while their parcel is picked, so a quirk can add its own goodies to the weighted pool,
/// as Meridian's quirk mail goodies do.
/// </summary>
[ByRefEvent]
public readonly record struct WGMailGoodiesEvent(Dictionary<string, float> Parcels);
