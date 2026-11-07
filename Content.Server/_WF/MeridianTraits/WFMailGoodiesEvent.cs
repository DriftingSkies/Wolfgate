namespace Content.Server._WF.MeridianTraits;

/// <summary>
/// Raised on a mail recipient while their parcel is picked, so a quirk can add its own goodies to the weighted pool,
/// as Meridian's quirk mail goodies do.
/// </summary>
[ByRefEvent]
public readonly record struct WFMailGoodiesEvent(Dictionary<string, float> Parcels);
