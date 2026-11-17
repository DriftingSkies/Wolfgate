namespace Content.Shared._WG.Species;

/// <summary>
/// Keeps a mob's thirst from falling below Okay, whatever drains it, so a species that never needs to drink never gets
/// thirsty or dries out.
/// </summary>
[RegisterComponent]
public sealed partial class WGNeverThirstyComponent : Component;
