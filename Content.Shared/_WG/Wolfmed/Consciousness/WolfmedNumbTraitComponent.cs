namespace Content.Shared._WG.Wolfmed.Consciousness;

/// <summary>
/// The Numb trait: the patient feels no pain, but pain still Downs and faints them. Numbness from drugs leaves
/// this off and keeps pain from putting the body down.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedNumbTraitComponent : Component;

/// <summary>Raised on a Numb trait body when its pain changed but what it feels did not.</summary>
[ByRefEvent]
public record struct WolfmedNumbPainChangedEvent;
